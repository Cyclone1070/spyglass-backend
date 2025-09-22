using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using spyglass_backend.Configuration;
using spyglass_backend.Features.Links;

namespace spyglass_backend.Features.Search
{
	public class SearchOrchestrationService(
		ILogger<SearchOrchestrationService> logger,
		IOptions<SearchSettings> searchSettings,
		SearchService searchService, // Inject the actual scraping service
		IServiceProvider serviceProvider)
	{
		private readonly ConcurrentDictionary<string, SearchStream> _activeSearches = new();
		private readonly ILogger<SearchOrchestrationService> _logger = logger;
		private readonly SearchSettings _searchSettings = searchSettings.Value;
		private readonly SearchService _searchService = searchService;
		private readonly IServiceProvider _serviceProvider = serviceProvider;

		public async IAsyncEnumerable<ResultDto> GetOrStartSearchStream(string query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			var searchStream = _activeSearches.GetOrAdd(query, q =>
			{
				_logger.LogInformation("Starting new search stream for query: {Query}", q);
				var newStream = new SearchStream();
				newStream.SearchTask = Task.Run(() => StartSearchInBackground(q, newStream));
				return newStream;
			});

			// First, yield any cached results
			foreach (var cachedResult in searchStream.GetCachedResults())
			{
				cancellationToken.ThrowIfCancellationRequested();
				yield return cachedResult;
			}
			// If the search is already completed, yield break
			if (searchStream.IsCompleted)
			{
				yield break;
			}
			// Otherwise, stream new results as they arrive
			await foreach (var result in searchStream.Reader.ReadAllAsync(cancellationToken))
			{
				yield return result;
			}
		}

		private async Task StartSearchInBackground(string query, SearchStream searchStream)
		{
			using var scope = _serviceProvider.CreateScope();
			var mongoResultService = scope.ServiceProvider.GetRequiredService<MongoResultService>();
			var mongoLinkService = scope.ServiceProvider.GetRequiredService<MongoLinkService>();

			try
			{
				using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_searchSettings.SearchTimeoutSecond));
				var cachedResult = await mongoResultService.GetAsync(query);
				if (cachedResult != null)
				{
					_logger.LogInformation("Found cached results in MongoDB for query: {Query}", query);
					foreach (var result in cachedResult.Results)
					{
						var resultDto = ToResultDto(result);
						searchStream.AddToCache(resultDto);
						await searchStream.Writer.WriteAsync(resultDto);
					}
					return;
				}

				var links = await mongoLinkService.GetAsync("responseTime", SortDirection.Ascending);
				var results = new List<Result>();
				var resultStream = _searchService.SearchLinksAsync(query, links, timeoutCts.Token);

				await foreach (var result in resultStream)
				{
					results.Add(result);
					var resultDto = ToResultDto(result);
					searchStream.AddToCache(resultDto);
					await searchStream.Writer.WriteAsync(resultDto);
				}

				// After all results are in, sort the cached results by score
				var sortedResults = results.OrderByDescending(r => r.Score).ToList();
				searchStream.SortCacheByScore();

				var storedResult = new StoredResult
				{
					Query = query,
					Results = sortedResults,
					CreatedAt = DateTime.UtcNow
				};
				await mongoResultService.CreateAsync(storedResult);
				_logger.LogInformation("Stored results for query: {Query}", query);

			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error during search for query: {Query}", query);
			}
			finally
			{
				searchStream.Writer.TryComplete();
				searchStream.IsCompleted = true;
				// Consider a mechanism to clean up old streams from the dictionary.
				_ = Task.Run(async () =>
				{
					// Wait for a grace period (e.g., 5 minutes) to allow any late-joining clients
					// to fetch the cached results before removing the stream from memory.
					await Task.Delay(TimeSpan.FromMinutes(_searchSettings.CacheDurationMinute));
					if (_activeSearches.TryRemove(query, out _))
					{
						_logger.LogInformation("Cleaned up completed search stream for query: {Query}", query);
					}
				});
			}
		}
		private static ResultDto ToResultDto(Result result)
		{
			return new ResultDto
			{
				Title = result.Title,
				ResultUrl = result.ResultUrl,
				Category = result.Category,
				WebsiteStarred = result.WebsiteStarred,
				WebsiteTitle = result.WebsiteTitle,
				SearchUrl = result.SearchUrl,
				Score = result.Score,
				ImageUrl = result.ImageUrl,
				AltText = result.AltText
			};
		}
	}
}
