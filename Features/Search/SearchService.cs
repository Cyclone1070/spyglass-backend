using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AngleSharp;
using spyglass_backend.Features.Links;
using spyglass_backend.Features.WebUtils;

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
			var normalisedQuery = ResultATagService.NormaliseString(query);

			_ = Task.Run(async () =>
			{
				// Configure the parallelism options.
				var parallelOptions = new ParallelOptions
				{
					MaxDegreeOfParallelism = 50 // Set your desired concurrency limit here (e.g., 10)
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
			var cards = document.QuerySelectorAll(link.CardSelector);
			foreach (var card in cards)
			{
				if (card.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
				{
					var cardUrl = ResultATagService.ToAbsoluteUrl(link.Url, card.GetAttribute("href"));
					if (cardUrl == null) continue;
					yield return CreateResult(
						link: link,
						title: ResultATagService.CleanTitle(card.TextContent),
						resultUrl: cardUrl,
						score: ResultATagService.GetRankingScore(normalisedQuery, card.TextContent),
						year: DateTime.Now.Year.ToString(),
						imageUrl: ResultATagService.ToAbsoluteUrl(link.Url, card.QuerySelector("img")?.GetAttribute("src")));
				}

				var aTags = card.QuerySelectorAll("a[href]");
				if (aTags.Length == 0) continue;
				// Use the first <a> tag with an href attribute
				var resultUrl = ResultATagService.ToAbsoluteUrl(link.Url, aTags[0].GetAttribute("href"));
				if (resultUrl == null) continue;
				string? rawTitle = null;
				foreach (var aTag in aTags.Skip(1))
				{
					if (aTag.GetAttribute("href") == aTags[0].GetAttribute("href") && !string.IsNullOrWhiteSpace(aTag.TextContent.Trim()))
					{
						rawTitle = aTag.TextContent;
						break;
					}
				}
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

				var imgUrl = ResultATagService.ToAbsoluteUrl(link.Url, card.QuerySelector("img")?.GetAttribute("src"));

				yield return CreateResult(
					link: link,
					title: finalTitle,
					resultUrl: resultUrl,
					score: finalScore,
					year: DateTime.Now.Year.ToString(),
					imageUrl: imgUrl);
			}
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
