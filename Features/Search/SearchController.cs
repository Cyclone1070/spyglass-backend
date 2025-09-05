using Microsoft.AspNetCore.Mvc;

namespace spyglass_backend.Features.Search
{
	[ApiController]
	[Route("api/[controller]")]
	public class SearchController(
			ILogger<SearchController> logger) : ControllerBase
	{
		private readonly ILogger<SearchController> _logger = logger;

		[HttpGet]
		[ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public IActionResult Get()
		{
			_logger.LogInformation("Received request to search.");
			try
			{
				// Placeholder for actual search logic
				return Ok("Search functionality is under development.");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error occurred while processing search request.");
				return StatusCode(500, "Internal server error");
			}
		}
	}
}
