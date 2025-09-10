namespace spyglass_backend.Features.Search
{
	public record Result
	{
		public required string Title { get; init; }
		public required string ResultUrl { get; init; }
		public required string WebsiteTitle { get; init; }
		public required string WebsiteUrl { get; init; }
		public required bool WebsiteStarred { get; init; }
		public required int Score { get; init; }
		public string? Year { get; init; }
		public string Type => GetType().Name;
		public string? ImageUrl { get; init; }
	};
	public record BookResult : Result
	{
		public string? Format { get; init; }
		public string? Author { get; init; }
		public string? Language { get; init; }
	}
	public record MovieResult : Result
	{
		public string? Director { get; init; }
	}
	public record GameResult : Result
	{
		public string? Platform { get; init; }
		public string? Size { get; init; }
	}
}
