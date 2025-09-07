using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;

namespace spyglass_backend.Features.Links
{
	public class MegathreadService(
		ILogger<MegathreadService> logger,
		IOptions<ScraperRules> rules,
		WebsiteLinkService websiteLinkService,
		SearchLinkService searchLinkService,
		ResultCardSelectorService resultCardSelectorService)
	{
		private readonly ILogger<MegathreadService> _logger = logger;
		private readonly ScraperRules _rules = rules.Value;
		private readonly WebsiteLinkService _websiteLinkService = websiteLinkService;
		private readonly SearchLinkService _searchLinkService = searchLinkService;
		private readonly ResultCardSelectorService _resultCardSelectorService = resultCardSelectorService;

		private static int _concurrentSearchLinkOperations = 0;
		private static int _concurrentResultCardSelectorOperations = 0;

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

			_logger.LogDebug("Starting {Count} ScrapeSearchLinksAsync operations.", websiteLinks.Count);
			var SearchLinksTasks = websiteLinks.Select(async websiteLink =>
			{
				var count = Interlocked.Increment(ref _concurrentSearchLinkOperations);
				_logger.LogDebug("ScrapeSearchLinksAsync starting. Concurrent operations: {Count}", count);
				try
				{
					return await _searchLinkService.ScrapeSearchLinksAsync(websiteLink);
				}
				catch (InvalidOperationException e)
				{
					_logger.LogWarning(e, "Could not generate search links for {Url}", websiteLink.Url);
					return null;
				}
				catch (Exception e)
				{
					_logger.LogError(e, "Error generating search links for {Url}", websiteLink.Url);
					return null;
				}
				finally
				{
					var countAfter = Interlocked.Decrement(ref _concurrentSearchLinkOperations);
					_logger.LogDebug("ScrapeSearchLinksAsync finished. Concurrent operations: {Count}. Url: {Url}.", countAfter, websiteLink.Url);
				}
			});
			var searchLinks = (await Task.WhenAll(SearchLinksTasks))
				.Where(link => link != null)
				.Select(link => link!)
				.ToList();

			_logger.LogDebug("Starting {Count} FindResultCardSelectorAsync operations.", searchLinks.Count);
			var resultCardSelectorTasks = searchLinks.Select(async searchLink =>
			{
				var count = Interlocked.Increment(ref _concurrentResultCardSelectorOperations);
				_logger.LogDebug("FindResultCardSelectorAsync starting. Concurrent operations: {Count}", count);
				try
				{
					return await _resultCardSelectorService.FindResultCardSelectorAsync(searchLink);
				}
				catch (InvalidOperationException e)
				{
					_logger.LogWarning(e, "Could not find result card selector for {Url}", searchLink.Url);
					return null;
				}
				catch (Exception e)
				{
					_logger.LogError(e, "Error finding result card selector for {SearchUrl}", searchLink.SearchUrl);
					return null;
				}
				finally
				{
					var countAfter = Interlocked.Decrement(ref _concurrentResultCardSelectorOperations);
					_logger.LogDebug("FindResultCardSelectorAsync finished. Concurrent operations: {Count}. Url: {Url}.", countAfter, searchLink.Url);
				}
			});
			var links = (await Task.WhenAll(resultCardSelectorTasks))
				.Where(link => link != null)
				.Select(link => link!)
				.ToList();

			return links;
		}
	}
}
