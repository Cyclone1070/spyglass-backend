using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;

namespace spyglass_backend.Features.Links
{
	public class WebsiteLinkService(
		ILogger<WebsiteLinkService> logger,
		IHttpClientFactory httpClientFactory,
		IOptions<ScraperRules> rules)
	{
		private readonly ILogger<WebsiteLinkService> _logger = logger;
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
		private readonly ScraperRules _rules = rules.Value;



		public async Task<IEnumerable<WebsiteLink>> ScrapeWebsiteLinksAsync(string Url)
		{
			_logger.LogInformation("Scraping {Url}...", Url);
			// Create an HttpClient using the factory. This is a best practice.
			var client = _httpClientFactory.CreateClient();
			var htmlContent = await client.GetStringAsync(Url);

			var context = BrowsingContext.New(AngleSharp.Configuration.Default);
			var document = await context.OpenAsync(req => req.Content(htmlContent));

			// Use LINQ to query the document in a declarative way.
			// SelectMany "flattens" the results. We get a list of categories,
			// each containing a list of links, and this turns it into one big list of links.
			var allLinks = _rules.Categories
				.SelectMany(category =>
				{
					var headerElements = document.QuerySelectorAll(category.Selector);
					return headerElements.SelectMany(header => ScrapeLinksFromHeader(header, category.Name));
				})
				.ToList();

			_logger.LogInformation("Scraping complete. Found {LinkCount} total links.", allLinks.Count);
			return allLinks;
		}

		// private helpers
		// This method is responsible for scraping links from a specific header element.
		private IEnumerable<WebsiteLink> ScrapeLinksFromHeader(IElement headerElement, string categoryName)
		{

			var listElement = headerElement.NextElementSibling;
			while (listElement != null && listElement.TagName != "UL")
			{
				listElement = listElement.NextElementSibling;
			}

			if (listElement == null)
			{
				// Return an empty list of links if no <ul> is found.
				return [];
			}

			// Use LINQ to find, filter, and transform the link elements into our WebsiteLink model.
			return listElement.QuerySelectorAll("li a")
				.Select(linkElement => new { Element = linkElement, ParentLi = linkElement.Closest("li") })
				.Where(x => IsValidLink(x.Element, x.ParentLi))
				.Select(x => new WebsiteLink
				{
					Title = x.Element.TextContent.Trim(),
					Url = x.Element.GetAttribute("href") ?? string.Empty, // Ensure Url is never null
					Category = categoryName,
					Starred = x.ParentLi?.ClassList.Contains("starred") ?? false // Safe navigation for null
				});
		}

		// This method checks if a link is valid based on several criteria like skip keywords.
		private bool IsValidLink(IElement linkElement, IElement? parentLi)
		{
			if (parentLi == null || parentLi.QuerySelector(".i-twemoji-globe-with-meridians") != null)
				return false;

			if (int.TryParse(linkElement.TextContent.Trim(), out _))
				return false;

			var linkUrl = linkElement.GetAttribute("href");
			var parentLiText = parentLi.TextContent;

			if (string.IsNullOrWhiteSpace(linkUrl))
				return false;

			// Use LINQ's .Any() for a clean, case-insensitive keyword check.
			return !_rules.SkipKeywords.Any(keyword =>
				linkUrl.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
				parentLiText.Contains(keyword, StringComparison.OrdinalIgnoreCase));
		}


	}
}