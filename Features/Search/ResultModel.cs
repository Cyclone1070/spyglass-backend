using spyglass_backend.Features.Links;

namespace spyglass_backend.Features.Search
{
	public record Result
	{
		public required string Title { get; init; }
		public required Link Link { get; init; }
		public required string Year { get; init; }
		public string Type => GetType().Name;
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
