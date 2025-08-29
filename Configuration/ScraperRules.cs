namespace spyglass_backend.Configuration;

public class ScraperRules
{
	public required List<string> MegathreadUrl { get; set; }
	public required List<string> SkipKeywords { get; set; }
	public required List<CategoryRule> Categories { get; set; }
}

public class CategoryRule
{
	public required string Name { get; set; }
	public required string Selector { get; set; }
}
