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
			MongoLinkService mongoLinkService,
			MongoResultService mongoResultService) : ControllerBase
	{
		private readonly ILogger<SearchController> _logger = logger;
		private readonly SearchService _searchService = searchService;
		private readonly MongoLinkService _mongoLinkService = mongoLinkService;
		private readonly MongoResultService _mongoResultService = mongoResultService;

		[HttpGet]
		public IActionResult Get([FromQuery] string q)
		{
			_logger.LogInformation("Received request to search.");
			var cancellationToken = HttpContext.RequestAborted;

			async IAsyncEnumerable<ResultDto> StreamAndSaveSearchResults()
			{
				var cachedResult = await _mongoResultService.GetAsync(q);
				if (cachedResult != null)
				{
					_logger.LogInformation("Returning cached results for query: {Query}", q);
					foreach (var result in cachedResult.Results)
					{
						yield return new ResultDto
						{
							Title = result.Title,
							ResultUrl = result.ResultUrl,
							Category = result.Category,
							WebsiteTitle = result.WebsiteTitle,
							WebsiteUrl = result.WebsiteUrl,
							WebsiteStarred = result.WebsiteStarred,
							Score = result.Score,
							Year = result.Year,
							ImageUrl = result.ImageUrl,
							AltText = result.AltText
						};
					}
					yield break;
				}
				var links = await _mongoLinkService.GetAsync("responseTime", SortDirection.Ascending);
				var results = new List<Result>();
				var resultStream = _searchService.SearchLinksAsync(q, links, cancellationToken);

				await foreach (var result in resultStream.WithCancellation(cancellationToken))
				{
					results.Add(result);
					yield return new ResultDto
					{
						Title = result.Title,
						ResultUrl = result.ResultUrl,
						Category = result.Category,
						WebsiteTitle = result.WebsiteTitle,
						WebsiteUrl = result.WebsiteUrl,
						WebsiteStarred = result.WebsiteStarred,
						Score = result.Score,
						Year = result.Year,
						ImageUrl = result.ImageUrl,
						AltText = result.AltText
					};
				}

				if (!cancellationToken.IsCancellationRequested)
				{
					var storedResult = new StoredResult
					{
						Query = q,
						Results = results,
						CreatedAt = DateTime.UtcNow
					};
					await _mongoResultService.CreateAsync(storedResult);
					_logger.LogInformation("Stored results for query: {Query}", q);
				}
				else
				{
					_logger.LogInformation("Search was cancelled, not storing results for query: {Query}", q);
				}
			}

			return new NdjsonStreamResult<ResultDto>(StreamAndSaveSearchResults());
		}
	}
}
