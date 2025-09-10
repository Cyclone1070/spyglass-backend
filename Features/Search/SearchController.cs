using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using spyglass_backend.Features.Links;

namespace spyglass_backend.Features.Search
{
	[ApiController]
	[Route("api/[controller]")]
	public class SearchController(
			ILogger<SearchController> logger,
			SearchService searchService,
			MongoLinkService mongoLinkService) : ControllerBase
	{
		private readonly ILogger<SearchController> _logger = logger;
		private readonly SearchService _searchService = searchService;
		private readonly MongoLinkService _mongoLinkService = mongoLinkService;

		[HttpGet]
		public async Task<IActionResult> Get([FromQuery] string q)
		{
			_logger.LogInformation("Received request to search.");
			var links = await _mongoLinkService.GetAsync("responseTime", SortDirection.Ascending);

			var cancellationToken = HttpContext.RequestAborted;
			var resultStream = _searchService.SearchLinksAsync(q, links, cancellationToken);

			return new NdjsonStreamResult<Result>(resultStream);
		}
	}
}
