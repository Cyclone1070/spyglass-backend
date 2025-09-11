using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace spyglass_backend.Features.Search
{
	[BsonKnownTypes(typeof(BookResult), typeof(MovieResult), typeof(GameResult))]
	public record Result
	{
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string Id { get; init; } = null!;
		[BsonElement("title")]
		public required string Title { get; init; }
		[BsonElement("ResultUrl")]
		public required string ResultUrl { get; init; }
		[BsonElement("websiteTitle")]
		public required string WebsiteTitle { get; init; }
		[BsonElement("websiteUrl")]
		public required string WebsiteUrl { get; init; }
		[BsonElement("websiteStarred")]
		public required bool WebsiteStarred { get; init; }
		[BsonElement("score")]
		public required int Score { get; init; }
		[BsonElement("year")]
		public string? Year { get; init; }
		[BsonElement("type")]
		public string Type => GetType().Name;
		[BsonElement("imageUrl")]
		public string? ImageUrl { get; init; }
	};
	public record BookResult : Result
	{
		[BsonElement("format")]
		public string? Format { get; init; }
		[BsonElement("author")]
		public string? Author { get; init; }
		[BsonElement("language")]
		public string? Language { get; init; }
	}
	public record MovieResult : Result
	{
		[BsonElement("director")]
		public string? Director { get; init; }
	}
	public record GameResult : Result
	{
		[BsonElement("platform")]
		public string? Platform { get; init; }
		[BsonElement("size")]
		public string? Size { get; init; }
	}
	public record StoredResult
	{
		[BsonId]
		public required string Query { get; init; }
		[BsonElement("results")]
		public required List<Result> Results { get; init; }
		[BsonElement("createdAt")]
		public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
	}
}
