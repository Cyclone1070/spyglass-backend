using AngleSharp.Dom;

namespace spyglass_backend.Features.Links
{
	public record WebsiteLink
	{
		public required string Title { get; set; }
		public required string Url { get; set; }
		public required string Category { get; set; }
		public required bool Starred { get; set; }
	}

	public record SearchLink : WebsiteLink
	{
		public required string SearchUrl { get; set; }
	}
	public record Link : SearchLink
	{
		public required string Selector { get; set; }
	}

	// Records for finding card css selectors
	public record ElementSelector(string Parent, string Element)
	{
		public override string ToString() => $"{Parent} > {Element}";
	};
	public record RepeatingPattern(string Parent, List<IElement> Elements, int Count) { }

	// Json serialization links storage
	public record StoredLinks(
			int WebsiteLinksCount,
			int SearchLinksCount,
			int ValidLinksCount,
			Dictionary<string, List<Link>> Links)
	{ }
}