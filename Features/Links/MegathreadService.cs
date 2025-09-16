using System.Collections.Concurrent;
using System.Web;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Results;

namespace spyglass_backend.Features.Links
{
	public class MegathreadService(
		ILogger<MegathreadService> logger,
		IOptions<ScraperRules> rules,
		WebService webService,
		WebsiteLinkService websiteLinkService,
		SearchLinkService searchLinkService)
	{
		private readonly ILogger<MegathreadService> _logger = logger;
		private readonly ScraperRules _rules = rules.Value;
		private readonly WebService _webService = webService;
		private readonly WebsiteLinkService _websiteLinkService = websiteLinkService;
		private readonly SearchLinkService _searchLinkService = searchLinkService;

		// Main entry point. Orchestrates the scraping strategy.
		public async Task<List<Link>> ScrapeMegathreadAsync()
		{
			_logger.LogInformation("Starting megathread scraping...");
			var websiteLinksTask = _rules.MegathreadUrls.Select(async link =>
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

			// 1. Configure the parallelism options.
			var parallelOptions = new ParallelOptions
			{
				MaxDegreeOfParallelism = 80 // Set your concurrency limit here
			};

			// 2. Use Parallel.ForEachAsync to iterate and process the collection.
			await Parallel.ForEachAsync(websiteLinks, parallelOptions, async (websiteLink, cancellationToken) =>
			{
				try
				{
					var searchLink = await _searchLinkService.ScrapeSearchLinksAsync(websiteLink);
					searchLinks.Add(searchLink);

					// Get the queries
					string[] queries = _rules.CardFindingQueries.ValidQueries.TryGetValue(websiteLink.Category, out var categoryQueries)
						? categoryQueries
						: ["the", "of"];
					// Get the blacklist element selectors
					var noResultUrl = string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(_rules.CardFindingQueries.InvalidQuery));
					var (noResultDoc, noResultResponseTime) = await _webService.GetHtmlDocumentAsync(noResultUrl);
					var noResultBlacklist = noResultDoc.All
						.Select(WebService.GetElementSelector)
						.Where(s => !string.IsNullOrEmpty(s.Element)) // Filter out empty selectors
						.ToHashSet();

					// Get the 2 documents with results
					var (withResultsDoc1, withResultsResponseTime1) = await _webService.GetHtmlDocumentAsync(string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(queries[0])));
					var (withResultsDoc2, withResultsResponseTime2) = await _webService.GetHtmlDocumentAsync(string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(queries[1])));
					var averageResponseTime = (noResultResponseTime + withResultsResponseTime1 + withResultsResponseTime2) / 3;

					// Find the result card selector
					var resultCardSelector = ResultCardService.FindResultCardSelector(noResultBlacklist, withResultsDoc1, withResultsDoc2);

					finalLinks.Add(CreateLink(searchLink, resultCardSelector.ToString(), averageResponseTime));
				}
				catch (InvalidOperationException e)
				{
					_logger.LogWarning(e, "Could not complete operation for {Url}", websiteLink.Url);
				}
				catch (Exception e)
				{
					_logger.LogError(e, "An unexpected error occurred during operation for {Url}", websiteLink.Url);
				}
			});

			var links = finalLinks.DistinctBy(link => link.Url).ToList();

			_logger.LogInformation("Scraped {WebsiteLinkCount} website links, resulting in {SearchLinkCount} search links and {FinalLinkCount} final links.",
				websiteLinks.Count, searchLinks.Count, links.Count);

			return links;
		}

		private static Link CreateLink(SearchLink searchLink, string selector, long averageResponseTime) => new()
		{
			Title = searchLink.Title,
			Url = searchLink.Url,
			Category = searchLink.Category,
			Starred = searchLink.Starred,
			SearchUrl = searchLink.SearchUrl,
			CardSelector = selector,
			ResponseTime = averageResponseTime
		};
	}
}
