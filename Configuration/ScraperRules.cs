namespace spyglass_backend.Configuration;

public record ScraperRules
{
	public required List<string> MegathreadUrls { get; set; }
	public required List<string> SkipKeywords { get; set; }
	public required List<CategoryRule> Categories { get; set; }
	public required CardFindingQueries CardFindingQueries { get; set; }
}

public record CategoryRule
{
	public required string Name { get; set; }
	public required string Selector { get; set; }
}

public record CardFindingQueries
{
	public required string InvalidQuery { get; set; }
	public required Dictionary<string, string[]> ValidQueries { get; set; }
}
