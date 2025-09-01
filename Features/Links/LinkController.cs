using Microsoft.AspNetCore.Mvc;

namespace spyglass_backend.Features.Links;

[ApiController]
[Route("api/[controller]")]
public class LinkController(
	ILogger<LinkController> logger,
	WebsiteLinkService websiteLinkService,
	SearchLinkService searchLinkService,
	ResultCardSelectorService resultCardSelectorService) : ControllerBase
{
	private readonly ILogger<LinkController> _logger = logger;
	private readonly WebsiteLinkService _websiteLinkService = websiteLinkService;
	private readonly SearchLinkService _searchLinkService = searchLinkService;
	private readonly ResultCardSelectorService _resultCardSelectorService = resultCardSelectorService;

	[HttpGet]
	public async Task<IActionResult> GetLinks([FromQuery] string Url)
	{
		if (string.IsNullOrWhiteSpace(Url))
		{
			return BadRequest("Url cannot be null or empty.");
		}

		try
		{
			var websiteLinks = await _websiteLinkService.ScrapeWebsiteLinksAsync(Url);
			if (websiteLinks == null || !websiteLinks.Any())
			{
				return NotFound("No links found on the specified URL.");
			}
			var processingTasks = websiteLinks.Select(async websiteLink =>
			{
				try
				{
					// The entire two-step process for a single link is encapsulated here.
					var searchLink = await _searchLinkService.ScrapeSearchLinksAsync(websiteLink);
					if (searchLink == null)
					{
						// This link couldn't be processed into a SearchLink, so skip it.
						return null;
					}

					var finalLink = await _resultCardSelectorService.FindResultCardSelectorAsync(searchLink);
					return finalLink;
				}
				catch (Exception ex)
				{
					// Log the error for the specific link that failed.
					_logger.LogWarning(ex, "Failed to process link: {Url}", websiteLink.Url);

					// Return null for this failed task so Task.WhenAll doesn't fail.
					return null;
				}
			});
			var results = await Task.WhenAll(processingTasks);
			var validLinks = results.Where(link => link != null).ToList();
			if (validLinks.Count == 0)
			{
				return NotFound("None of the links could be processed successfully.");
			}
			return Ok(new { ValidCount = validLinks.Count, Total = websiteLinks.Count(), Links = validLinks });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to scrape links from {Url}", Url);
			return StatusCode(500, "An error occurred while scraping links.");
		}
	}
}
