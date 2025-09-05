using AngleSharp.Dom;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace spyglass_backend.Features.Links
{
	public record WebsiteLink
	{
		[BsonElement("title")]
		public required string Title { get; set; }
		[BsonElement("url")]
		public required string Url { get; set; }
		[BsonElement("category")]
		public required string Category { get; set; }
		[BsonElement("starred")]
		public required bool Starred { get; set; }
	}

	public record SearchLink : WebsiteLink
	{
		[BsonElement("searchUrl")]
		public required string SearchUrl { get; set; }
	}
	public record Link : SearchLink
	{
		[BsonId]
		[BsonRepresentation(BsonType.ObjectId)]
		public string Id { get; set; } = null!;
		[BsonElement("selector")]
		public required string Selector { get; set; }
	}

	// Records for finding card css selectors
	public record ElementSelector(string Parent, string Element)
	{
		public override string ToString() => $"{Parent} > {Element}";
	};
	public record RepeatingPattern(string Parent, List<IElement> Elements, int Count) { }
}
