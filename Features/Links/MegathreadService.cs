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

		// Main entry point. Orchestrates the scraping strategy.
		public async Task<StoredLinks> ScrapeMegathreadAsync()
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

			var SearchLinksTasks = websiteLinks.Select(async websiteLink =>
			{
				try
				{
					return await _searchLinkService.ScrapeSearchLinksAsync(websiteLink);
				}
				catch (Exception e)
				{
					_logger.LogError(e, "Error generating search links for {Url}", websiteLink.Url);
					return null;
				}
			});
			var searchLinks = (await Task.WhenAll(SearchLinksTasks))
				.Where(link => link != null)
				.Select(link => link!)
				.ToList();

			var resultCardSelectorTasks = searchLinks.Select(async searchLink =>
			{
				try
				{
					return await _resultCardSelectorService.FindResultCardSelectorAsync(searchLink);
				}
				catch (Exception e)
				{
					_logger.LogError(e, "Error finding result card selector for {SearchUrl}", searchLink.SearchUrl);
					return null;
				}
			});
			var links = (await Task.WhenAll(resultCardSelectorTasks))
				.Where(link => link != null)
				.Select(link => link!)
				.ToList();

			var finalLinks = links
				.GroupBy(link => link.Category)
				.ToDictionary(g => g.Key, g => g.ToList());
			return new StoredLinks(websiteLinks.Count, searchLinks.Count, links.Count, finalLinks);
		}
	}
}