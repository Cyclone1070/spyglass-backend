using spyglass_backend.Features.Links;

namespace spyglass_backend.Features.Search
{
	public class SearchService(
			ILogger<SearchService> logger,
			MongoLinkService mongoLinkService)
	{
		private readonly ILogger<SearchService> _logger = logger;
		private readonly MongoLinkService _mongoLinkService = mongoLinkService;

		public async Task<List<Result>> SearchLinksAsync(string query)
		{
			_logger.LogInformation("Searching links with query: {Query}", query);
			try
			{
				var links = await _mongoLinkService.GetAsync();
				return []; // Placeholder for actual search logic
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error occurred while searching links with query: {Query}", query);
				throw;
			}
		}
	}
}
