using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;

namespace spyglass_backend.Features.Links
{
    public class WebsiteLinkService(
        ILogger<WebsiteLinkService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<ScraperRules> rules
    )
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
            var allLinks = _rules
                .Categories.SelectMany(category =>
                {
                    var headerElements = document.QuerySelectorAll(category.Selector);
                    return headerElements.SelectMany(header =>
                        ScrapeLinksFromHeader(header, category.Name)
                    );
                })
                .ToList();

            _logger.LogInformation(
                "Scraping complete. Found {LinkCount} total links.",
                allLinks.Count
            );
            return allLinks;
        }

        // private helpers
        // This method is responsible for scraping links from a specific header element.
        private IEnumerable<WebsiteLink> ScrapeLinksFromHeader(
            IElement headerElement,
            string categoryName
        )
        {
            // Determine boundary heading tags based on the current heading level.
            // For h2: stop at next h2
            // For h3: stop at next h3 or h2
            // For h4: stop at next h4, h3, or h2
            var boundaryTags = headerElement.TagName.ToUpperInvariant() switch
            {
                "H2" => new[] { "H2" },
                "H3" => new[] { "H3", "H2" },
                "H4" => new[] { "H4", "H3", "H2" },
                _ => new[] { "H2", "H3", "H4", "H5", "H6" }
            };

            var allLinks = new List<WebsiteLink>();
            var current = headerElement.NextElementSibling;

            while (current != null)
            {
                // Stop if we hit a boundary heading
                if (boundaryTags.Contains(current.TagName, StringComparer.OrdinalIgnoreCase))
                {
                    break;
                }

                // If it's a <ul>, scrape its links
                if (string.Equals(current.TagName, "UL", StringComparison.OrdinalIgnoreCase))
                {
                    var links = current
                        .QuerySelectorAll("li a")
                        .Select(linkElement => new
                        {
                            Element = linkElement,
                            ParentLi = linkElement.Closest("li"),
                        })
                        .Where(x => IsValidLink(x.Element, x.ParentLi))
                        .Select(x => new WebsiteLink
                        {
                            Title = x.Element.TextContent.Trim(),
                            Url = x.Element.GetAttribute("href") ?? string.Empty,
                            Category = categoryName,
                            Starred = x.ParentLi?.ClassList.Contains("starred") ?? false,
                        });

                    allLinks.AddRange(links);
                }

                current = current.NextElementSibling;
            }

            return allLinks;
        }

        // This method checks if a link is valid based on several criteria like skip keywords.
        private bool IsValidLink(IElement linkElement, IElement? parentLi)
        {
            if (
                parentLi == null
                || parentLi.QuerySelector(".i-twemoji-globe-with-meridians") != null
            )
            {
                return false;
            }

            if (int.TryParse(linkElement.TextContent.Trim(), out _))
                return false;

            var linkUrl = linkElement.GetAttribute("href");

            if (string.IsNullOrWhiteSpace(linkUrl))
                return false;

            // Use LINQ's .Any() for a clean, case-insensitive keyword check.
            return !_rules.MegathreadSkipKeywords.Any(keyword =>
                linkUrl.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || linkElement.TextContent.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            );
        }
    }
}
