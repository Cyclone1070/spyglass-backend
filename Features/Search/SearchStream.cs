using System.Threading.Channels;

namespace spyglass_backend.Features.Search
{
	// A class to hold the state of an ongoing search stream.
	public class SearchStream
	{
		private readonly Channel<ResultDto> _channel = Channel.CreateUnbounded<ResultDto>();
		private List<ResultDto> _cachedResults = [];
		private readonly Lock _cacheLock = new();

		public ChannelWriter<ResultDto> Writer => _channel.Writer;
		public ChannelReader<ResultDto> Reader => _channel.Reader;
		public Task? SearchTask { get; set; }
		public bool IsCompleted { get; set; }

		public void AddToCache(ResultDto result)
		{
			lock (_cacheLock)
			{
				_cachedResults.Add(result);
			}
		}

		public List<ResultDto> GetCachedResults()
		{
			lock (_cacheLock)
			{
				return [.. _cachedResults];
			}
		}
		public void SortCacheByScore()
		{
			lock (_cacheLock)
			{
				// OrderByDescending returns a new sorted sequence,
				// so we create a new list from it and replace the old one.
				_cachedResults = [.. _cachedResults.OrderByDescending(r => r.Score)];
			}
		}
	}
}
