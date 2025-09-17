using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace spyglass_backend.Features.Search
{
	public record Result
	{
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string Id { get; init; } = null!;
		[BsonElement("title")]
		public required string Title { get; init; }
		[BsonElement("resultUrl")]
		public required string ResultUrl { get; init; }
		[BsonElement("category")]
		public required string Category { get; init; }
		[BsonElement("websiteTitle")]
		public required string WebsiteTitle { get; init; }
		[BsonElement("websiteUrl")]
		public required string WebsiteUrl { get; init; }
		[BsonElement("websiteStarred")]
		public required bool WebsiteStarred { get; init; }
		[BsonElement("score")]
		public required int Score { get; init; }
		[BsonElement("year")]
		public int? Year { get; init; }
		[BsonElement("imageUrl")]
		public string? ImageUrl { get; init; }
	};

	public record ResultDto
	{
		public required string Title { get; init; }
		public required string ResultUrl { get; init; }
		public required string Category { get; init; }
		public required string WebsiteTitle { get; init; }
		public required string WebsiteUrl { get; init; }
		public required bool WebsiteStarred { get; init; }
		public required int Score { get; init; }
		public int? Year { get; init; }
		public string? ImageUrl { get; init; }
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
