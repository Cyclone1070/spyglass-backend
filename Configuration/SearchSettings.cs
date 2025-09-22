namespace spyglass_backend.Configuration
{
	public record SearchSettings
	{
		public required int MaxParallelism { get; init; }
		public required int CacheDurationMinute { get; init; }
		public required int SearchTimeoutSecond { get; init; }
	}
}
