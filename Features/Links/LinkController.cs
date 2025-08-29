using Microsoft.AspNetCore.Mvc;

namespace spyglass_backend.Features.Links;

[ApiController]
[Route("api/[controller]")]
public class LinkController(
	ILogger<LinkController> logger,
	WebsiteLinkService websiteLinkService,
	SearchLinkService searchLinkService) : ControllerBase
{
	private readonly ILogger<LinkController> _logger = logger;
	private readonly WebsiteLinkService _websiteLinkService = websiteLinkService;
	private readonly SearchLinkService _searchLinkService = searchLinkService;

	[HttpGet]
	public async Task<IActionResult> GetLinks([FromQuery] string Url)
	{
		if (string.IsNullOrWhiteSpace(Url))
		{
			return BadRequest("Url cannot be null or empty.");
		}

		try
		{
			var links = await _websiteLinkService.ScrapeWebsiteLinksAsync(Url);
			if (links == null || !links.Any())
			{
				return NotFound("No links found on the specified URL.");
			}
			var searchLink = await _searchLinkService.ScrapeSearchLinksAsync(links.First());
			if (searchLink == null)
			{
				return NotFound("No search link found on the specified URL.");
			}
			return Ok(new { Links = links, SearchLink = searchLink });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to scrape links from {Url}", Url);
			return StatusCode(500, "An error occurred while scraping links.");
		}
	}
}
