using System.Text.RegularExpressions;
using System.Threading.Channels;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;
using spyglass_backend.Features.WebUtils;

namespace spyglass_backend.Features.Search
{
    public class SearchService(
        ILogger<SearchService> logger,
        IOptions<ScraperRules> scraperRules,
        IOptions<SearchSettings> searchSettings,
        IWebService webService
    )
    {
        private readonly ILogger<SearchService> _logger = logger;
        private readonly ScraperRules _scraperRules = scraperRules.Value;
        private readonly SearchSettings _searchSettings = searchSettings.Value;
        private readonly IWebService _webService = webService;

        public IAsyncEnumerable<Result> SearchLinksAsync(string normalisedQuery, List<Link> links)
        {
            var channel = Channel.CreateUnbounded<Result>();

            _ = Task.Run(async () =>
            {
                // Configure the parallelism options.
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _searchSettings.MaxParallelism,
                };

                try
                {
                    await Parallel.ForEachAsync(
                        links,
                        parallelOptions,
                        async (link, _) =>
                        {
                            try
                            {
                                await foreach (var result in ScrapeLinkAsync(normalisedQuery, link))
                                {
                                    // Put the found item on the conveyor belt
                                    await channel.Writer.WriteAsync(result, CancellationToken.None);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                _logger.LogInformation(
                                    "Scraping for link {LinkUrl} was cancelled.",
                                    link.Url
                                );
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to scrape link {LinkUrl}", link.Url);
                            }
                        }
                    );
                }
                finally
                {
                    // Signal that no more items will be written to the channel
                    channel.Writer.Complete();
                }
            });

            // Return the reader side of the channel as an async enumerable
            return channel.Reader.ReadAllAsync();
        }

        private async IAsyncEnumerable<Result> ScrapeLinkAsync(string normalisedQuery, Link link)
        {
            var queryUrl = string.Format(link.SearchUrl, Uri.EscapeDataString(normalisedQuery));

            IDocument document;
            try
            {
                (document, _) = await _webService.GetHtmlDocumentAsync(
                    queryUrl,
                    referer: new Uri(link.Url)
                );
            }
            catch (HttpRequestException e)
                when (e.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Got 403 for search on {Url}. Retrying through proxy relay...",
                    link.Url
                );
                (document, _) = await _webService.GetHtmlDocumentAsync(
                    queryUrl,
                    referer: new Uri(link.Url),
                    useProxy: true
                );
            }

            var cards = document.QuerySelectorAll(link.CardSelector);

            // 1. Create a dictionary to count how many DISTINCT cards each URL appears in.
            var urlToDistinctCardCount = new Dictionary<string, int>();

            foreach (var card in cards)
            {
                var distinctUrlsInCurrentCard = GetCardUrls(card);

                foreach (var url in distinctUrlsInCurrentCard)
                {
                    if (urlToDistinctCardCount.TryGetValue(url, out int count))
                    {
                        urlToDistinctCardCount[url] = count + 1;
                    }
                    else
                    {
                        urlToDistinctCardCount[url] = 1;
                    }
                }
            }

            // 2. Determine which URLs are truly unique across ALL cards
            var uniqueUrlsAcrossCards = urlToDistinctCardCount
                .Where(pair => pair.Value == 1)
                .Select(pair => pair.Key)
                .ToHashSet();

            foreach (var card in cards)
            {
                var cardUrls = GetCardUrls(card);
                var firstUniqueUrl = cardUrls.FirstOrDefault(u => uniqueUrlsAcrossCards.Contains(u));
                if (firstUniqueUrl == null)
                {
                    _logger.LogWarning("No unique link found in card from {LinkUrl}", link.Url);
                    continue;
                }

                // Check category skip keywords
                var segments = firstUniqueUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 2)
                {
                    var secondToLastSegment = segments[^2];
                    if (_scraperRules.SearchSkipKeywords.Any(keyword =>
                        secondToLastSegment.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }

                string? resultUrl = ResultATagService.ToAbsoluteUrl(link.Url, firstUniqueUrl);
                if (resultUrl == null)
                    continue;

                string? rawTitle = null;

                if (ExtractUrlFromElement(card) == firstUniqueUrl)
                {
                    rawTitle = card.TextContent;
                }
                else
                {
                    foreach (var el in card.QuerySelectorAll("*"))
                    {
                        if (ExtractUrlFromElement(el) == firstUniqueUrl && !string.IsNullOrWhiteSpace(el.TextContent.Trim()))
                        {
                            rawTitle = el.TextContent;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(rawTitle))
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

                var imgElement = card.QuerySelector("img");

                yield return CreateResult(
                    link: link,
                    title: finalTitle,
                    resultUrl: resultUrl,
                    score: finalScore,
                    imageUrl: ResultCardService.GetImageUrlFromElement(link.Url, imgElement),
                    altText: imgElement?.GetAttribute("alt")
                );
            }
        }

        private static List<string> GetCardUrls(IElement card)
        {
            var urls = new List<string>();

            var selfUrl = ExtractUrlFromElement(card);
            if (!string.IsNullOrEmpty(selfUrl))
            {
                urls.Add(selfUrl);
            }

            foreach (var el in card.QuerySelectorAll("*"))
            {
                var url = ExtractUrlFromElement(el);
                if (!string.IsNullOrEmpty(url))
                {
                    urls.Add(url);
                }
            }

            return urls.Distinct().ToList();
        }

        private static string? ExtractUrlFromElement(IElement el)
        {
            if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
            {
                var href = el.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href)) return href;
            }

            foreach (var attr in (string[])["data-href", "data-link", "data-url"])
            {
                var val = el.GetAttribute(attr);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            var onclick = el.GetAttribute("onclick");
            if (!string.IsNullOrWhiteSpace(onclick))
            {
                var match = Regex.Match(onclick, @"(?:location(?:\.href)?\s*=\s*['""])([^'""\s]+)['""]");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        private static Result CreateResult(
            Link link,
            string title,
            string resultUrl,
            int score,
            string? imageUrl = null,
            string? altText = null
        )
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
                AltText = altText,
            };
        }
    }
}
