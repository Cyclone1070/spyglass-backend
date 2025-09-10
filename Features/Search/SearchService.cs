using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using AngleSharp;
using FuzzySharp;
using spyglass_backend.Features.Links;

namespace spyglass_backend.Features.Search
{
	public partial class SearchService(
			ILogger<SearchService> logger,
			IHttpClientFactory httpClientFactory)
	{
		private readonly ILogger<SearchService> _logger = logger;
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

		public IAsyncEnumerable<Result> SearchLinksAsync(string query, List<Link> links, CancellationToken cancellationToken = default)
		{
			var channel = Channel.CreateUnbounded<Result>();
			var normalisedQuery = NormaliseString(query);

			_ = Task.Run(async () =>
			{
				// Configure the parallelism options.
				var parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = 90 // Set your desired concurrency limit here (e.g., 10)
				};

				try
				{
					await Parallel.ForEachAsync(links, parallelOptions, async (link, innerCancellationToken) =>
					{
						// Use the combined cancellation token
						using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, innerCancellationToken);
						var currentCancellationToken = linkedCts.Token;

						try
						{
							var results = ScrapeLinkAsync(normalisedQuery, link, currentCancellationToken);
							await foreach (var result in results.WithCancellation(currentCancellationToken))
							{
								// Put the found item on the conveyor belt
								await channel.Writer.WriteAsync(result, currentCancellationToken);
							}
						}
						catch (OperationCanceledException)
						{
							_logger.LogInformation("Scraping for link {LinkUrl} was cancelled.", link.Url);
						}
						catch (Exception ex)
						{
							_logger.LogError(ex, "Failed to scrape link {LinkUrl}", link.Url);
						}
					});
				}
				finally
				{
					// === SECTION 4: Announce the Shift is Over ===
					channel.Writer.Complete();
				}
			}, cancellationToken);

			// === SECTION 5: Give the Packer the End of the Conveyor Belt ===
			return channel.Reader.ReadAllAsync(cancellationToken);
		}

		private async IAsyncEnumerable<Result> ScrapeLinkAsync(string normalisedQuery, Link link, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			var client = _httpClientFactory.CreateClient();

			var queryUrl = string.Format(link.SearchUrl, Uri.EscapeDataString(normalisedQuery));
			var htmlResponse = await client.GetStringAsync(queryUrl, cancellationToken);
			// Read response content
			var context = BrowsingContext.New(AngleSharp.Configuration.Default);
			var document = await context.OpenAsync(req => req.Content(htmlResponse), cancellationToken);
			var cards = document.QuerySelectorAll(link.Selector);
			foreach (var card in cards)
			{
				var aTags = card.QuerySelectorAll("a");
				foreach (var aTag in aTags)
				{
					// First, validate the link's URL. This is a cheap check.
					var absoluteUrl = ToAbsoluteUrl(link.Url, aTag.GetAttribute("href"));
					if (absoluteUrl is null) { continue; }

					// 1. Find the best possible title and score from within this <a> tag.
					var bestCandidate = aTag.QuerySelectorAll("*") // Get all children
						.Where(el => !string.Equals(el.TagName, "SCRIPT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(el.TextContent))
						// If the <a> tag has no children with text, we fall back to using the aTag itself.
						.DefaultIfEmpty(aTag)
						.Select(el => new
						{
							Text = CleanTitle(el.TextContent),
							Score = GetRankingScore(normalisedQuery, NormaliseString(el.TextContent))
						})
						.Where(c => !string.IsNullOrWhiteSpace(c.Text))
						.MaxBy(c => c.Score);

					// 2. Now, use this single, accurate score to make a decision.
					if (bestCandidate is not null && bestCandidate.Score >= 79)
					{
						yield return CreateResult(
							link: link,
							title: bestCandidate.Text,
							resultUrl: absoluteUrl,
							score: bestCandidate.Score,
							year: DateTime.Now.Year.ToString(),
							imageUrl: ToAbsoluteUrl(link.Url, card.QuerySelector("img")?.GetAttribute("src")));
					}
				}
			}
		}

		// REGEX 1: Matches anything that ISN'T a letter, number, or space.
		[GeneratedRegex(@"[^a-z0-9\s]")]
		private static partial Regex PunctuationRegex();

		// REGEX 2: Matches one or MORE whitespace characters in a row.
		[GeneratedRegex(@"\s+")]
		private static partial Regex WhitespaceRegex();

		private static string NormaliseString(string input)
		{
			if (string.IsNullOrWhiteSpace(input)) return string.Empty;

			var lowercased = input.ToLowerInvariant();

			// STEP 1: Use the first Regex to remove all punctuation.
			var noPunctuation = PunctuationRegex().Replace(lowercased, "");

			// STEP 2: Use the second Regex to clean up and standardize spaces.
			var singleSpaced = WhitespaceRegex().Replace(noPunctuation, " ").Trim();

			return singleSpaced;
		}

		private static string CleanTitle(string title)
		{
			if (string.IsNullOrWhiteSpace(title)) return string.Empty;

			// Remove extra spaces created by phrase removal
			title = WhitespaceRegex().Replace(title, " ").Trim();

			return title;
		}

		private static string? ToAbsoluteUrl(string baseUrl, string? url)
		{
			// 1. Basic input validation.
			if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(url))
			{
				return null;
			}

			// 2. Safely create the base Uri. This is required for resolving relative URLs.
			if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
			{
				// The provided base URL was invalid.
				return null;
			}

			// 3. Let the Uri class do all the intelligent work.
			// This constructor is designed for this exact scenario and handles all cases.
			if (Uri.TryCreate(baseUri, url, out Uri? absoluteUri))
			{
				return absoluteUri.AbsoluteUri;
			}

			// Return null if the combination failed for any reason (e.g., a malformed relative URL).
			return null;
		}
		private static int GetRankingScore(string normalisedQuery, string normalisedTitle)
		{
			int score = Fuzz.WeightedRatio(normalisedQuery, normalisedTitle);

			var queryWords = normalisedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var titleWords = new HashSet<string>(normalisedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries));
			if (queryWords.Any(queryWord => !titleWords.Contains(queryWord)))
			{
				score -= 1;
			}

			return score;
		}
		private static Result CreateResult(Link link, string title, string resultUrl, int score, string year, string? imageUrl = null)
		{

			return link.Category switch
			{
				"Books" => new BookResult
				{
					Title = title,
					ResultUrl = resultUrl,
					WebsiteUrl = link.Url,
					WebsiteTitle = link.Title,
					WebsiteStarred = link.Starred,
					Score = score,
					Year = year,
					ImageUrl = imageUrl
				},
				"Movies" => new MovieResult
				{
					Title = title,
					ResultUrl = resultUrl,
					WebsiteUrl = link.Url,
					WebsiteTitle = link.Title,
					WebsiteStarred = link.Starred,
					Score = score,
					Year = year,
					ImageUrl = imageUrl
				},
				"Games Download" => new GameResult
				{
					Title = title,
					ResultUrl = resultUrl,
					WebsiteUrl = link.Url,
					WebsiteTitle = link.Title,
					WebsiteStarred = link.Starred,
					Score = score,
					Year = year,
					ImageUrl = imageUrl
				},
				// The underscore _ is the equivalent of the 'default' case
				_ => new Result
				{
					Title = title,
					ResultUrl = resultUrl,
					WebsiteUrl = link.Url,
					WebsiteTitle = link.Title,
					WebsiteStarred = link.Starred,
					Score = score,
					Year = year,
					ImageUrl = imageUrl
				}
			};
		}
	}
}
