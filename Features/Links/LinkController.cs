using Microsoft.AspNetCore.Mvc;

namespace spyglass_backend.Features.Links
{
	[ApiController]
	[Route("api/[controller]")]
	public class LinkController(
		ILogger<LinkController> logger,
		MegathreadService megathreadService,
		LinkExportService linkExportService) : ControllerBase
	{
		private readonly ILogger<LinkController> _logger = logger;
		private readonly MegathreadService _megathreadService = megathreadService;
		private readonly LinkExportService _linkExportService = linkExportService;

		[HttpPost]
		[ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<IActionResult> PostLinks()
		{
			_logger.LogInformation("Received request to scrape megathread.");
			try
			{
				var processedLinks = await _megathreadService.ScrapeMegathreadAsync();

				var filePath = await _linkExportService.SaveJsonFileAsync(processedLinks, "links.json");

				return Ok(new
				{
					Message = "File successfully generated and saved on the server.",
					FilePath = filePath
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