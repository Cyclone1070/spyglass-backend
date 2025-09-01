namespace spyglass_backend.Features.Links;

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
