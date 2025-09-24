using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AngleSharp;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;
using spyglass_backend.Features.WebUtils;

namespace spyglass_backend.Features.Search
{
	public partial class SearchService(
			ILogger<SearchService> logger,
			IOptions<ScraperRules> scraperRules,
			IOptions<SearchSettings> searchSettings,
			IHttpClientFactory httpClientFactory)
	{
		private readonly ILogger<SearchService> _logger = logger;
		private readonly ScraperRules _scraperRules = scraperRules.Value;
		private readonly SearchSettings _searchSettings = searchSettings.Value;
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

		public IAsyncEnumerable<Result> SearchLinksAsync(string normalisedQuery, List<Link> links, CancellationToken cancellationToken = default)
		{
			var channel = Channel.CreateUnbounded<Result>();

			_ = Task.Run(async () =>
			{
				// Configure the parallelism options.
				var parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = _searchSettings.MaxParallelism // Set your desired concurrency limit here (e.g., 10)
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
					// Signal that no more items will be written to the channel
					channel.Writer.Complete();
				}
			}, cancellationToken);

			// Return the reader side of the channel as an async enumerable
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
			var cards = document.QuerySelectorAll(link.CardSelector);

			// 1. Create a dictionary to count how many DISTINCT cards each href appears in.
			var hrefToDistinctCardCount = new Dictionary<string, int>();

			foreach (var card in cards)
			{
				// Get all <a> tags with href attributes within the current card
				var aTagsInCard = card.QuerySelectorAll("a[href]");

				// Extract all UNIQUE hrefs WITHIN THIS specific card
				// This ensures that if an href appears multiple times in the same card, it's counted only once for that card.
				var distinctHrefsInCurrentCard = aTagsInCard
					.Select(a => a.GetAttribute("href"))
					.Where(href => href != null)
					.Select(href => href!) // Non-nullable now
					.Distinct() // Only count each href once PER CARD
					.ToList();

				// For each unique href found in this card, increment its count in the dictionary
				foreach (var href in distinctHrefsInCurrentCard)
				{
					// Use TryGetValue to avoid double lookup and handle potential missing keys gracefully
					if (hrefToDistinctCardCount.TryGetValue(href, out int count))
					{
						hrefToDistinctCardCount[href] = count + 1;
					}
					else
					{
						hrefToDistinctCardCount[href] = 1;
					}
				}
			}

			// 2. Determine which hrefs are truly unique across ALL cards (i.e., they appeared in only ONE distinct card).
			var uniqueUrlsAcrossCards = hrefToDistinctCardCount
										.Where(pair => pair.Value == 1) // Keep hrefs that appeared in exactly one distinct card
										.Select(pair => pair.Key) // Get the unique href string
										.ToHashSet(); // Store in a HashSet for efficient lookup			

			foreach (var card in cards)
			{
				// Handle the case where the card itself is an <a> tag
				if (card.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
				{
					string? currentCardHref = card.GetAttribute("href");
					if (currentCardHref == null || !uniqueUrlsAcrossCards.Contains(currentCardHref))
					{
						_logger.LogWarning("No unique link found in card from {LinkUrl}", link.Url);
						continue; // Skip this card if its href is not unique or missing 
					}

					string? resultUrl = ResultATagService.ToAbsoluteUrl(link.Url, currentCardHref);
					if (resultUrl == null) continue;

					var imgElement = card.QuerySelector("img");

					yield return CreateResult(
						link: link,
						title: ResultATagService.CleanTitle(card.TextContent),
						resultUrl: resultUrl,
						score: ResultATagService.GetRankingScore(normalisedQuery, ResultATagService.NormaliseString(card.TextContent)),
						imageUrl: ResultCardService.GetImageUrlFromElement(link.Url, imgElement),
						altText: imgElement?.GetAttribute("alt")
						);
					continue;
				}
				else
				{
					var aTags = card.QuerySelectorAll("a[href]");
					if (aTags.Length == 0) continue;
					// Use the first <a> tag with an href attribute
					var firstUniqueATag = aTags
						.Where(a =>
						{
							// check for category links
							var currentUrl = a.GetAttribute("href");
							if (string.IsNullOrWhiteSpace(currentUrl))
							{
								return false;
							}
							var segments = currentUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
							if (segments.Length < 2)
							{
								return true; // Not enough segments to determine category, assume valid
							}
							var secondToLastSegment = segments[^2];

							return !_scraperRules.SearchSkipKeywords.Any(keyword => secondToLastSegment.Contains(keyword, StringComparison.OrdinalIgnoreCase));
						})
						.FirstOrDefault(a =>
					{
						var href = a.GetAttribute("href");
						return href != null && uniqueUrlsAcrossCards.Contains(href);
					});
					if (firstUniqueATag == null)
					{
						_logger.LogWarning("No unique link found in card from {LinkUrl}", link.Url);
						continue;
					}

					var resultUrl = ResultATagService.ToAbsoluteUrl(link.Url, firstUniqueATag.GetAttribute("href"));
					if (resultUrl == null) continue;

					// Attempt to find a better title from other <a> tags or headings within the card
					string? rawTitle = null;
					foreach (var aTag in aTags)
					{
						if (aTag.GetAttribute("href") == firstUniqueATag.GetAttribute("href") && !string.IsNullOrWhiteSpace(aTag.TextContent.Trim()))
						{
							rawTitle = aTag.TextContent;
							break;
						}
					}
					// If no suitable <a> tag text found, look for headings or fallback to card text
					if (rawTitle == null)
					{
						if (card.QuerySelector("h1") != null && !string.IsNullOrWhiteSpace(card.QuerySelector("h1")?.TextContent.Trim()))
							rawTitle = card.QuerySelector("h1")!.TextContent;
						else if (card.QuerySelector("h2") != null && !string.IsNullOrWhiteSpace(card.QuerySelector("h2")?.TextContent.Trim()))
							rawTitle = card.QuerySelector("h2")!.TextContent;
						else if (card.QuerySelector("h3") != null && !string.IsNullOrWhiteSpace(card.QuerySelector("h3")?.TextContent.Trim()))
							rawTitle = card.QuerySelector("h3")!.TextContent;
						else
							rawTitle = card.TextContent;
					}
					// Score the title vs URL and pick the better one
					string normalisedTitle = ResultATagService.NormaliseString(rawTitle);
					int titleScore = ResultATagService.GetRankingScore(normalisedQuery, normalisedTitle);

					string extractedUrl = ResultATagService.ExtractUrlPath(resultUrl);
					int urlScore = ResultATagService.GetRankingScore(normalisedQuery, ResultATagService.NormaliseString(extractedUrl));

					string finalTitle;
					int finalScore;

					if (urlScore > titleScore)
					{
						finalTitle = ResultATagService.CleanTitle(extractedUrl);
						finalScore = urlScore;
					}
					else
					{
						finalTitle = ResultATagService.CleanTitle(rawTitle);
						finalScore = titleScore;
					}

					var imgElement = card.QuerySelector("img");

					yield return CreateResult(
						link: link,
						title: finalTitle,
						resultUrl: resultUrl,
						score: finalScore,
						imageUrl: ResultCardService.GetImageUrlFromElement(link.Url, imgElement),
						altText: imgElement?.GetAttribute("alt"));
				}
			}
		}

		private static Result CreateResult(Link link, string title, string resultUrl, int score, string? imageUrl = null, string? altText = null)
		{
			return new Result
			{
				Title = title,
				ResultUrl = resultUrl,
				WebsiteTitle = link.Title,
				SearchUrl = link.SearchUrl,
				WebsiteStarred = link.Starred,
				Score = score,
				Category = link.Category,
				ImageUrl = imageUrl,
				AltText = altText
			};
		}
	}
}
