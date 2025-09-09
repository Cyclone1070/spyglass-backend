using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using Microsoft.Extensions.Options;
using spyglass_backend.Configuration;

namespace spyglass_backend.Features.Links
{
	public partial class ResultCardSelectorService(
		ILogger<ResultCardSelectorService> logger,
		IHttpClientFactory httpClientFactory,
		IOptions<ScraperRules> rules)
	{
		private readonly ILogger<ResultCardSelectorService> _logger = logger;
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
		private readonly CardFindingQueries _queries = rules.Value.CardFindingQueries;

		// Main entry point. Orchestrates the two-tiered scraping strategy.
		public async Task<Link> FindResultCardSelectorAsync(SearchLink searchLink)
		{
			var noResultsUrl = string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(_queries.InvalidQuery));
			var (noResultsDoc, noResultsResponseTime) = await GetHtmlDocumentAsync(noResultsUrl);
			var noResultsBlacklist = noResultsDoc.All
				.Select(GetElementSelector)
				.Where(s => !string.IsNullOrEmpty(s.Element)) // Filter out empty selectors
				.ToHashSet();

			try
			{
				string[] queries = _queries.ValidQueries.TryGetValue(searchLink.Category, out var query)
					? query
					: ["the", "of"]; // Fallback query if none defined for category

				_logger.LogInformation("Attempting differential scraping for category '{Category}'.", searchLink.Category);
				// Find the repeating pattern, do it twice to get a wider variety of cards in case some idiot fucked up their html
				var (withResultsDoc1, withResultsResponseTime1) = await GetHtmlDocumentAsync(string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(queries[0])));
				var pattern1 = PerformDiffAnalysis(withResultsDoc1, noResultsBlacklist);

				var (withResultsDoc2, withResultsResponseTime2) = await GetHtmlDocumentAsync(string.Format(searchLink.SearchUrl, HttpUtility.UrlEncode(queries[1])));
				var pattern2 = PerformDiffAnalysis(withResultsDoc2, noResultsBlacklist);

				if (pattern1.Parent != pattern2.Parent)
				{
					throw new InvalidOperationException("Differential scraping found inconsistent patterns.");
				}
				var selector = GetCommonSelector(pattern1.Parent, pattern1.Elements.Concat(pattern2.Elements));
				var averageResponseTime = (noResultsResponseTime + withResultsResponseTime1 + withResultsResponseTime2) / 3;
				return CreateLink(searchLink, selector.ToString(), averageResponseTime);
			}
			catch (Exception e)
			{
				throw new InvalidOperationException("Differential scraping attempt failed.", e);
			}
		}

		// Finds repeating element patterns that do NOT exist on the blacklist.
		private static RepeatingPattern PerformDiffAnalysis(IDocument withResultsDoc, HashSet<ElementSelector> blacklist)
		{
			// Filter elements to only those present in the 'diff' selector set.
			var candidateElements = withResultsDoc.All
					.Where(el => el.ParentElement != null &&
								 !blacklist.Contains(GetElementSelector(el)))
					.ToList();
			var validPatterns = candidateElements
				.GroupBy(el => el.ParentElement) // Group siblings together
				.SelectMany(siblingGroup =>
					// Inside each sibling group, find elements with identical child structures.
					siblingGroup
						.GroupBy(el => string.Join("", el.Children.Select(c => c.TagName))) // Key: "DIVSPANIMG"
																							// 3. Apply the final filtering criteria.
						.Where(patternGroup =>
							patternGroup.Count() > 1 && // It must be a repeating pattern.
							patternGroup.First().Children.Length > 0 && // Must have children.
							patternGroup.First().QuerySelector("a") != null // A descendant must be an <a> tag.
						)
						// 4. Project the valid groups into our record for scoring.
						.Select(validGroup =>
						{
							var elements = validGroup.ToList(); // Iterate ONCE to create the list
							return new RepeatingPattern
							{
								Parent = BuildParentSelector(siblingGroup.Key!),
								Elements = elements,           // Use the created list
								Count = elements.Count         // Use the list's .Count property (instant) };
							};
						})
				)
				.ToList();

			var bestPattern = validPatterns
					.Where(p => !p.Elements.Any(IsPaginationCard))
					.Select(p => new
					{
						Pattern = p,
						Score = (p.Count * 10) + CalculateComplexityScore(p.Elements.First())
					})
					.OrderByDescending(x => x.Score)
					.First().Pattern;
			return bestPattern;
		}

		// A heuristic to score how "complex" an element is, to differentiate simple dividers from rich content cards.
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

		// =================================================================
		// UTILITY HELPERS
		// =================================================================

		private async Task<(IDocument, long)> GetHtmlDocumentAsync(string url)
		{
			var client = _httpClientFactory.CreateClient("ScraperClient");
			var stopwatch = Stopwatch.StartNew();
			var htmlContent = await client.GetStringAsync(url);
			stopwatch.Stop();
			var context = BrowsingContext.New(AngleSharp.Configuration.Default);
			var document = await context.OpenAsync(req => req.Content(htmlContent));
			return (document, stopwatch.ElapsedMilliseconds);
		}



		private static ElementSelector GetElementSelector(IElement element)
		{
			// Build the selector part for the current element.
			var elementSelector = BuildClassSelector(element);

			// Get the parent element.
			var parent = element.ParentElement;

			// If there's no parent (e.g., for the <html> tag), the selector is just the element itself.
			if (parent == null)
			{
				return new ElementSelector
				{
					Parent = string.Empty,
					Element = elementSelector
				};
			}

			// Build the selector part for the parent.
			var parentSelector = BuildParentSelector(parent);

			// Combine them using the direct child combinator ">".
			return new ElementSelector
			{
				Parent = parentSelector,
				Element = elementSelector
			};
		}

		// Creates a generalized CSS selector for a group of elements by finding their common classes.
		private static ElementSelector GetCommonSelector(string parent, IEnumerable<IElement> elements)
		{
			var first = elements.First();
			var tagName = first.TagName.ToLowerInvariant();

			// Find the intersection of all class lists to get the classes they all share.
			var commonClasses = elements
				.Select(e => e.ClassList as IEnumerable<string>) // Cast to IEnumerable for Aggregate
				.Aggregate((current, next) => current.Intersect(next))
				.OrderBy(c => c)
				.ToList();

			var builder = new StringBuilder(tagName);
			if (commonClasses.Count > 0)
			{
				var escapedCommonClasses = commonClasses.Select(EscapeCssIdentifier);
				builder.Append('.').Append(string.Join(".", escapedCommonClasses));
			}
			return new ElementSelector
			{
				Parent = parent,
				Element = builder.ToString()
			};
		}

		// Helper function to build a consistent "tag.class1.class2" selector for a single element.
		private static string BuildClassSelector(IElement element)
		{
			if (element == null) return string.Empty;

			var tag = element.TagName.ToLowerInvariant();
			var builder = new StringBuilder(tag);

			// --- MODIFIED LOGIC ---
			var classes = element.ClassList
				.Select(EscapeCssIdentifier) // Escape each class name
				.OrderBy(c => c)
				.ToList();

			if (classes.Count > 0)
			{
				// Priority 1: If classes exist, use them. They are more likely to be semantic.
				builder.Append('.').Append(string.Join(".", classes));
			}
			else if (!string.IsNullOrEmpty(element.Id))
			{
				// Priority 2: If NO classes exist, fall back to using the ID.
				builder.Append('#').Append(element.Id);
			}

			// If neither classes nor ID exist, it will just return the tag name.
			return builder.ToString();
		}
		// Prioritise ID over classes
		private static string BuildParentSelector(IElement element)
		{
			if (element == null) return string.Empty;
			// Handle the priority case: if a non-empty ID exists, use it immediately.
			if (!string.IsNullOrEmpty(element.Id))
			{
				return $"{element.TagName.ToLowerInvariant()}#{element.Id}";
			}
			// If there's no ID, fall back to the original class-based logic.
			// This avoids repeating the class-building code.
			return BuildClassSelector(element);
		}
		[GeneratedRegex(@"[^a-zA-Z0-9_-]")]
		private static partial Regex InvalidCssCharRegex();

		private static string EscapeCssIdentifier(string identifier)
		{
			if (string.IsNullOrEmpty(identifier))
			{
				return string.Empty;
			}

			// This regex matches any character that is NOT a-z, A-Z, 0-9, underscore, or hyphen.
			// The replacement pattern "\\$&" inserts a literal backslash before the matched character.
			return InvalidCssCharRegex().Replace(identifier, @"\$&");
		}


		// Heuristically determines if an individual element is a pagination item by its content.
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

		private static Link CreateLink(SearchLink searchLink, string selector, long averageResponseTime) => new()
		{
			Title = searchLink.Title,
			Url = searchLink.Url,
			Category = searchLink.Category,
			Starred = searchLink.Starred,
			SearchUrl = searchLink.SearchUrl,
			Selector = selector,
			ResponseTime = averageResponseTime
		};
	}
}
