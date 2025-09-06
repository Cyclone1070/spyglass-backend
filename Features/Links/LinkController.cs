using Microsoft.AspNetCore.Mvc;

namespace spyglass_backend.Features.Links
{
	[ApiController]
	[Route("api/[controller]")]
	public class LinkController(
		ILogger<LinkController> logger,
		MegathreadService megathreadService,
		MongoLinkService mongoLinkService) : ControllerBase
	{
		private readonly ILogger<LinkController> _logger = logger;
		private readonly MegathreadService _megathreadService = megathreadService;
		private readonly MongoLinkService _mongoLinkService = mongoLinkService;

		[HttpPost]
		[ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> PostLinks()
		{
			_logger.LogInformation("Received request to scrape megathread.");
			try
			{
				await _mongoLinkService.RemoveAllAsync();

				var allLinks = await _megathreadService.ScrapeMegathreadAsync();

				await _mongoLinkService.CreateManyAsync(allLinks);

				return Ok(new
				{
					Message = $"Successfully scraped and saved {allLinks.Count} links to the database."
				});
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error occurred while scraping megathread.");
				return StatusCode(500, "Internal server error");
			}
		}
	}
}
