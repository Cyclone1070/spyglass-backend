using AngleSharp.Dom;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace spyglass_backend.Features.Links
{
	public record WebsiteLink
	{
		[BsonElement("title")]
		public required string Title { get; init; }
		[BsonElement("url")]
		public required string Url { get; init; }
		[BsonElement("category")]
		public required string Category { get; init; }
		[BsonElement("starred")]
		public required bool Starred { get; init; }
	}

	public record SearchLink : WebsiteLink
	{
		[BsonElement("searchUrl")]
		public required string SearchUrl { get; init; }
	}
	public record Link : SearchLink
	{
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string Id { get; init; } = null!;
		[BsonElement("selector")]
		public required string Selector { get; init; }
	}

	// Records for finding card css selectors
	public record ElementSelector
	{
		public required string Parent { get; init; }
		public required string Element { get; init; }

		public override string ToString() => $"{Parent} > {Element}";
	};
	public record RepeatingPattern
	{
		public required string Parent { get; init; }
		public required List<IElement> Elements { get; init; }
		public required int Count { get; init; }
	}
}
