using Microsoft.AspNetCore.Mvc;

namespace spyglass_backend.Features.Search
{
	[ApiController]
	[Route("api/[controller]")]
	public class SearchController(
			ILogger<SearchController> logger,
			SearchOrchestrationService searchOrchestrationService) : ControllerBase
	{
		private readonly ILogger<SearchController> _logger = logger;
		private readonly SearchOrchestrationService _searchOrchestrationService = searchOrchestrationService;

		[HttpGet]
		public IActionResult Get([FromQuery] string q)
		{
			_logger.LogInformation("Received search request for query: {Query}", q);
			var cancellationToken = HttpContext.RequestAborted;

			var stream = _searchOrchestrationService.GetOrStartSearchStream(q, cancellationToken);

			return new NdjsonStreamResult<ResultDto>(stream);
		}
	}
}
