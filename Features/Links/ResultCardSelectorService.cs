using System.Text;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;

namespace spyglass_backend.Features.Links;

public class ResultCardSelectorService(
	ILogger<ResultCardSelectorService> logger,
	IHttpClientFactory httpClientFactory,
	IOptions<ScraperRules> rules)
{
	private readonly ILogger<ResultCardSelectorService> _logger = logger;
	private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
	private readonly CardFindingQueries _queries = rules.Value.CardFindingQueries;

	// This blacklist prevents the scraper from identifying common UI list items as result cards.
	private static readonly HashSet<string> IgnoredCardSignatures =
	[
		"option", "li", "tr", "td", "dd", "dt"
	];

	// A private record to cleanly hold information about a discovered repeating pattern.
	private record RepeatingPattern(IElement Parent, string CardSignature, int Count, IElement FirstCardInstance);

	/// <summary>
	/// Main entry point. Orchestrates the two-tiered scraping strategy.
	/// </summary>
	public async Task<Link> FindResultCardSelectorAsync(SearchLink searchLink)
	{
		try
		{
			_logger.LogInformation("Tier 1 (Differential Scrape) starting for {Url}...", searchLink.Url);
			var selector = await RunDifferentialScrapeAsync(searchLink);
			_logger.LogInformation("Tier 1 (Differential Scrape) succeeded for {Url}.", searchLink.Url);
			return CreateLink(searchLink, selector);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Tier 1 (Differential Scrape) failed. Falling back to Tier 2 for {Url}.", searchLink.Url);
			try
			{
				_logger.LogInformation("Tier 2 (Frequency Analysis) starting for {Url}...", searchLink.Url);
				var selector = await RunFrequencyAnalysisScrapeAsync(searchLink);
				_logger.LogInformation("Tier 2 (Frequency Analysis) succeeded for {Url}.", searchLink.Url);
				return CreateLink(searchLink, selector);
			}
			catch (Exception e)
			{
				_logger.LogError(e, "All scraping tiers failed for {Url}.", searchLink.Url);
				throw new Exception($"All scraping tiers failed for {searchLink.Url}: {e.Message}", e);
			}
		}
	}

	// =================================================================
	// TIER ORCHESTRATION
	// =================================================================

	/// <summary>
	/// Tier 1 Orchestrator: Handles query logic for the differential scrape.
	/// </summary>
	private async Task<string> RunDifferentialScrapeAsync(SearchLink searchLink)
	{
		var noResultsUrl = string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(_queries.InvalidQuery));
		var noResultsBlacklist = await GetElementSignatureSetAsync(noResultsUrl);

		if (_queries.SpecialisedQueries.TryGetValue(searchLink.Category, out var specialisedQuery))
		{
			try
			{
				_logger.LogInformation("Attempting scrape with SPECIALISED query for category '{Category}'.", searchLink.Category);
				var withResultsUrl = string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(specialisedQuery));
				var withResultsDoc = await GetHtmlDocumentAsync(withResultsUrl);
				return PerformDiffAnalysis(withResultsDoc, noResultsBlacklist);
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Specialised query failed. Falling back to COMMON query for category '{Category}'.", searchLink.Category);
			}
		}

		try
		{
			_logger.LogInformation("Attempting scrape with COMMON query for '{Url}'.", searchLink.Url);
			var withResultsUrl = string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(_queries.ValidQuery));
			var withResultsDoc = await GetHtmlDocumentAsync(withResultsUrl);
			return PerformDiffAnalysis(withResultsDoc, noResultsBlacklist);
		}
		catch (Exception e)
		{
			throw new InvalidOperationException("All differential scrape attempts failed (specialised and common).", e);
		}
	}

	/// <summary>
	/// Tier 2 Orchestrator: Handles query logic for the frequency analysis scrape.
	/// </summary>
	private async Task<string> RunFrequencyAnalysisScrapeAsync(SearchLink searchLink)
	{
		// For Tier 2, we reverse the logic: try the common query first because it's more likely to
		// have a variety of content (ads, other modules) that a highly specific query might not,
		// which helps the scoring algorithm make a better decision.
		try
		{
			_logger.LogInformation("Attempting frequency analysis with COMMON query for '{Url}'.", searchLink.Url);
			var withResultsUrl = string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(_queries.ValidQuery));
			var doc = await GetHtmlDocumentAsync(withResultsUrl);
			return PerformFrequencyAnalysis(doc);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Frequency analysis with common query failed. Trying specialised query for category '{Category}'.", searchLink.Category);
			if (_queries.SpecialisedQueries.TryGetValue(searchLink.Category, out var specialisedQuery))
			{
				var withResultsUrl = string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(specialisedQuery));
				var doc = await GetHtmlDocumentAsync(withResultsUrl);
				return PerformFrequencyAnalysis(doc);
			}
			throw new InvalidOperationException("All frequency analysis attempts failed (common and specialised).", ex);
		}
	}

	// =================================================================
	// CORE LOGIC HELPERS (Bottom-Up Strategy)
	// =================================================================

	/// <summary>
	/// Tier 1 Core Logic: Finds repeating element patterns that do NOT exist on the blacklist.
	/// </summary>
	private static string PerformDiffAnalysis(IDocument withResultsDoc, IReadOnlySet<string> blacklist)
	{
		var patterns = FindRepeatingPatterns(withResultsDoc);

		var diffPatterns = patterns
			.Where(p => !blacklist.Contains(p.CardSignature))
			.ToList();

		var validCandidates = patterns
			.Where(p => !blacklist.Contains(p.CardSignature))
			.OrderByDescending(p => p.Count)
			.ToList();

		if (validCandidates.Count == 0)
		{
			throw new InvalidOperationException("Diff analysis failed: no unique repeating patterns found that weren't on the no-results page.");
		}

		var bestPattern = validCandidates
				.Select(p => new
				{
					Pattern = p,
					Score = (p.Count * 10) + CalculateComplexityScore(p.FirstCardInstance)
				})
				.OrderByDescending(x => x.Score)
				.First().Pattern;
		var containerSignature = GetElementSignature(bestPattern.Parent);
		return $"{containerSignature} > {bestPattern.CardSignature}";
	}

	private static int CalculateComplexityScore(IElement element)
	{
		// A simple divider will have 0-1 children and minimal text.
		// A content card will have multiple children (divs, spans, links) and significant text.
		var childElementCount = element.Children.Length;
		var textLength = element.TextContent.Trim().Length;

		// The score is a weighted sum of repetition, child count, and text length.
		// We cap the text length bonus to prevent a single wall of text from dominating.
		return (childElementCount * 5) + (Math.Min(textLength, 500) / 5);
	}

	/// <summary>
	/// Tier 2 Core Logic: Finds the highest-scoring repeating pattern based on content heuristics.
	/// </summary>
	private static string PerformFrequencyAnalysis(IDocument doc)
	{
		var patterns = FindRepeatingPatterns(doc);
		if (patterns.Count == 0)
		{
			throw new InvalidOperationException("Frequency analysis failed: no repeating element patterns found.");
		}

		var scoredCandidates = patterns
			.Select(p =>
			{
				var score = p.Count * 10; // Base score for repetition count
				score += CalculateComplexityScore(p.FirstCardInstance);
				if (p.FirstCardInstance.QuerySelector("a[href]") != null) score += 100; // Huge bonus for links
				if (p.FirstCardInstance.QuerySelector("img") != null) score += 50; // Bonus for images
				score -= GetNodeDepth(p.Parent) * 2; // Penalize by depth
				return new { Pattern = p, Score = score };
			})
			.Where(c => c.Score > 0)
			.OrderByDescending(c => c.Score)
			.ToList();

		if (scoredCandidates.Count == 0)
		{
			throw new InvalidOperationException("Frequency analysis failed: no candidates scored high enough to be a valid result card.");
		}

		var bestCandidate = scoredCandidates.First();
		var containerSignature = GetElementSignature(bestCandidate.Pattern.Parent);
		return $"{containerSignature} > {bestCandidate.Pattern.CardSignature}";
	}

	/// <summary>
	/// Finds all groups of repeating sibling elements in a document. This is the heart of the bottom-up strategy.
	/// </summary>
	private static List<RepeatingPattern> FindRepeatingPatterns(IDocument doc)
	{
		var patterns = new List<RepeatingPattern>();
		foreach (var potentialParent in doc.All)
		{
			if (potentialParent.Children.Length < 2) continue;

			var repeatingChildrenGroups = potentialParent.Children
				.Select(GetElementSignature)
				.Where(s => !string.IsNullOrEmpty(s))
				.GroupBy(s => s)
				.Where(g => g.Count() > 1)
				.ToList();

			foreach (var group in repeatingChildrenGroups)
			{
				var cardSignature = group.Key;
				var tagName = cardSignature.Split('.')[0].Split('#')[0];
				if (IgnoredCardSignatures.Contains(tagName)) continue;

				var firstInstance = potentialParent.Children.First(c => GetElementSignature(c) == cardSignature);
				// Immediately disqualify patterns where the card itself looks like pagination.
				if (IsPaginationCard(firstInstance)) continue;

				patterns.Add(new RepeatingPattern(potentialParent, cardSignature, group.Count(), firstInstance));
			}
		}
		return patterns;
	}

	// =================================================================
	// UTILITY HELPERS
	// =================================================================

	private async Task<IDocument> GetHtmlDocumentAsync(string url)
	{
		var client = _httpClientFactory.CreateClient("ScraperClient");
		var htmlContent = await client.GetStringAsync(url);
		var context = BrowsingContext.New(AngleSharp.Configuration.Default);
		return await context.OpenAsync(req => req.Content(htmlContent));
	}

	private async Task<IReadOnlySet<string>> GetElementSignatureSetAsync(string url)
	{
		var doc = await GetHtmlDocumentAsync(url);
		return doc.All
			.Select(GetElementSignature)
			.Where(s => !string.IsNullOrEmpty(s))
			.ToHashSet();
	}

	private static string GetElementSignature(IElement element)
	{
		var tag = element.TagName.ToLowerInvariant();
		if (!string.IsNullOrEmpty(element.Id))
		{
			return $"{tag}#{element.Id}";
		}
		var builder = new StringBuilder(tag);
		var classes = element.ClassList.OrderBy(c => c);
		if (classes.Any())
		{
			builder.Append('.').Append(string.Join(".", classes));
		}
		return builder.ToString();
	}

	private static int GetNodeDepth(IElement node)
	{
		var depth = 0;
		var parent = node.ParentElement;
		while (parent != null)
		{
			depth++;
			parent = parent.ParentElement;
		}
		return depth;
	}

	/// <summary>
	/// Heuristically determines if an individual element is a pagination item by its content.
	/// </summary>
	private static bool IsPaginationCard(IElement element)
	{
		var text = element.TextContent.Trim();

		// Rule 1: Pagination items have short text content.
		if (string.IsNullOrEmpty(text) || text.Length > 25)
		{
			return false;
		}

		// Rule 2: The text is exactly a number (the strongest signal).
		if (int.TryParse(text, out _))
		{
			return true;
		}

		// Rule 3: The text is a common pagination keyword or symbol.
		string[] paginationKeywords = ["next", "prev", "previous", "last", "first", ">", "<", "»", "«"];
		return paginationKeywords.Any(key => text.Equals(key, StringComparison.OrdinalIgnoreCase));
	}

	private static Link CreateLink(SearchLink searchLink, string selector) => new()
	{
		Title = searchLink.Title,
		Url = searchLink.Url,
		Category = searchLink.Category,
		Starred = searchLink.Starred,
		SearchUrl = searchLink.SearchUrl,
		Selector = selector
	};
}
