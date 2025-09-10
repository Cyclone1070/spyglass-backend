using System.Collections.Concurrent;
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

		private static int _concurrentOperations = 0;

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
				MaxDegreeOfParallelism = 90 // Set your concurrency limit here
			};

			_logger.LogDebug("Starting {Count} combined operations with a concurrency limit of {Limit}.", websiteLinks.Count, parallelOptions.MaxDegreeOfParallelism);

			// 2. Use Parallel.ForEachAsync to iterate and process the collection.
			await Parallel.ForEachAsync(websiteLinks, parallelOptions, async (websiteLink, cancellationToken) =>
			{
				var count = Interlocked.Increment(ref _concurrentOperations);
				_logger.LogDebug("ScrapeSearchLinksAsync starting. Concurrent operations: {Count}", count);

				try
				{
					var searchLink = await _searchLinkService.ScrapeSearchLinksAsync(websiteLink);
					searchLinks.Add(searchLink);

					var link = await _resultCardSelectorService.FindResultCardSelectorAsync(searchLink);
					finalLinks.Add(link);
				}
				catch (InvalidOperationException e)
				{
					_logger.LogWarning(e, "Could not complete operation for {Url}", websiteLink.Url);
				}
				catch (Exception e)
				{
					_logger.LogError(e, "An unexpected error occurred during operation for {Url}", websiteLink.Url);
				}
				finally
				{
					count = Interlocked.Decrement(ref _concurrentOperations);
					_logger.LogDebug("ScrapeSearchLinksAsync completed. Concurrent operations: {Count}", count);
				}
			});

			var links = finalLinks.DistinctBy(link => link.Url).ToList();

			_logger.LogInformation("Scraped {WebsiteLinkCount} website links, resulting in {SearchLinkCount} search links and {FinalLinkCount} final links.",
				websiteLinks.Count, searchLinks.Count, links.Count);

			return links;
		}
	}
}
