using System.Collections.Concurrent;
using System.Web;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;
using spyglass_backend.Features.WebUtils;

namespace spyglass_backend.Features.Links
{
    public class MegathreadService(
        ILogger<MegathreadService> logger,
        IOptions<ScraperRules> scraperRules,
        IOptions<SearchSettings> searchSettings,
        IWebService webService,
        WebsiteLinkService websiteLinkService,
        SearchLinkService searchLinkService
    )
    {
        private readonly ILogger<MegathreadService> _logger = logger;
        private readonly ScraperRules _scraperRules = scraperRules.Value;
        private readonly SearchSettings _searchSettings = searchSettings.Value;
        private readonly IWebService _webService = webService;
        private readonly WebsiteLinkService _websiteLinkService = websiteLinkService;
        private readonly SearchLinkService _searchLinkService = searchLinkService;

        // Main entry point. Orchestrates the scraping strategy.
        public async Task<List<Link>> ScrapeMegathreadAsync()
        {
            _logger.LogInformation("Starting megathread scraping...");
            // Scrape website links from megathread URLs
            var websiteLinksTask = _scraperRules.MegathreadUrls.Select(async link =>
            {
                try
                {
                    return await _websiteLinkService.ScrapeWebsiteLinksAsync(link);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error scraping website links from {Url}", link);
                    return [];
                }
            });
            var websiteLinksNestedArray = await Task.WhenAll(websiteLinksTask);
            var websiteLinks = websiteLinksNestedArray
                .SelectMany(list => list)
                .DistinctBy(link => link.Url)
                .ToList();

            var searchLinks = new ConcurrentBag<SearchLink>();
            var finalLinks = new ConcurrentBag<Link>();

            var failedLinks = new ConcurrentBag<WebsiteLink>();

            // 1. Configure the parallelism options.
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = _searchSettings.MaxParallelism, // Set your concurrency limit here
            };

            // 2. First pass: Try all links normally
            _logger.LogInformation(
                "First pass: Attempting to scrape {Count} links normally...",
                websiteLinks.Count
            );
            await Parallel.ForEachAsync(
                websiteLinks,
                parallelOptions,
                async (websiteLink, _) =>
                {
                    try
                    {
                        await ProcessWebsiteLinkAsync(websiteLink, false, searchLinks, finalLinks);
                    }
                    catch (HttpRequestException e)
                        when (e.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        _logger.LogWarning(
                            "Got 403 for {Url}. Routing to proxy queue.",
                            websiteLink.Url
                        );
                        failedLinks.Add(websiteLink);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(
                            e,
                            "An error occurred during first pass for {Url}",
                            websiteLink.Url
                        );
                    }
                }
            );

            // 3. Second pass: Try failed links with proxy
            if (!failedLinks.IsEmpty)
            {
                _logger.LogInformation(
                    "Second pass: Retrying {Count} forbidden links through proxy...",
                    failedLinks.Count
                );
                var proxyOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = 40, // Concurrency limit for proxy
                };

                await Parallel.ForEachAsync(
                    failedLinks,
                    proxyOptions,
                    async (websiteLink, _) =>
                    {
                        try
                        {
                            await ProcessWebsiteLinkAsync(
                                websiteLink,
                                true,
                                searchLinks,
                                finalLinks
                            );
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(
                                e,
                                "Final failure for {Url} even through proxy.",
                                websiteLink.Url
                            );
                        }
                    }
                );
            }

            var links = finalLinks.DistinctBy(link => link.Url).ToList();

            _logger.LogInformation(
                "Scraped {WebsiteLinkCount} website links, resulting in {SearchLinkCount} search links and {FinalLinkCount} final links.",
                websiteLinks.Count,
                searchLinks.Count,
                links.Count
            );

            return links;
        }

        private async Task ProcessWebsiteLinkAsync(
            WebsiteLink websiteLink,
            bool useProxy,
            ConcurrentBag<SearchLink> searchLinks,
            ConcurrentBag<Link> finalLinks
        )
        {
            _logger.LogDebug(
                "Processing website link: {Url} (Proxy: {UseProxy})",
                websiteLink.Url,
                useProxy
            );

            var searchLink = await _searchLinkService.ScrapeSearchLinksAsync(websiteLink, useProxy);
            searchLinks.Add(searchLink);

            // Get the queries
            string[] queries = _scraperRules.CardFindingQueries.ValidQueries.TryGetValue(
                websiteLink.Category,
                out var categoryQueries
            )
                ? categoryQueries
                : ["the", "of"];
            // Get the blacklist element selectors
            var noResultUrl = string.Format(
                searchLink.SearchUrl,
                HttpUtility.UrlEncode(_scraperRules.CardFindingQueries.InvalidQuery)
            );
            var (noResultDoc, noResultResponseTime) = await _webService.GetHtmlDocumentAsync(
                noResultUrl,
                new Uri(websiteLink.Url),
                useProxy
            );
            var noResultBlacklist = noResultDoc
                .All.Select(e =>
                {
                    if (e.ParentElement == null)
                    {
                        return new ElementSelector
                        {
                            Parent = string.Empty,
                            Element = string.Empty,
                        };
                    }

                    var baseElementSelector = WebService.GetElementSelector(e);
                    var fullParentPath = WebService.GetTagPath(
                        noResultDoc.DocumentElement,
                        e.ParentElement
                    );
                    if (string.IsNullOrEmpty(fullParentPath))
                    {
                        return new ElementSelector
                        {
                            Parent = string.Empty,
                            Element = string.Empty,
                        };
                    }
                    return new ElementSelector
                    {
                        Parent = fullParentPath,
                        Element = baseElementSelector.Element,
                    };
                })
                .Where(s => !string.IsNullOrEmpty(s.Element)) // Filter out empty selectors
                .ToHashSet();

            // Get the 2 documents with results
            var (withResultsDoc1, withResultsResponseTime1) =
                await _webService.GetHtmlDocumentAsync(
                    string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(queries[0])),
                    new Uri(websiteLink.Url),
                    useProxy
                );
            var (withResultsDoc2, withResultsResponseTime2) =
                await _webService.GetHtmlDocumentAsync(
                    string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(queries[1])),
                    new Uri(websiteLink.Url),
                    useProxy
                );
            var averageResponseTime =
                (noResultResponseTime + withResultsResponseTime1 + withResultsResponseTime2) / 3;

            // Find the result card selector
            var resultCardSelector = ResultCardService.FindResultCardSelector(
                noResultBlacklist,
                withResultsDoc1,
                withResultsDoc2
            );

            finalLinks.Add(
                CreateLink(searchLink, resultCardSelector.ToString(), averageResponseTime)
            );
            _logger.LogInformation(
                "Successfully processed {Url} (Proxy: {UseProxy})",
                websiteLink.Url,
                useProxy
            );
        }

        private static Link CreateLink(
            SearchLink searchLink,
            string selector,
            long averageResponseTime
        ) =>
            new()
            {
                Title = searchLink.Title,
                Url = searchLink.Url,
                Category = searchLink.Category,
                Starred = searchLink.Starred,
                SearchUrl = searchLink.SearchUrl,
                CardSelector = selector,
                ResponseTime = averageResponseTime,
            };
    }
}
