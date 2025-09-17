using spyglass_backend.Features.Links;

using AngleSharp.Dom;
using System.Text.RegularExpressions;

namespace spyglass_backend.Features.WebUtils
{
	public partial class ResultCardService
	{
		public static ElementSelector FindResultCardSelector(HashSet<ElementSelector> noResultBlacklist, IDocument withResultsDoc1, IDocument withResultsDoc2)
		{
			// Find the repeating pattern, do it twice to get a wider variety of cards in case some idiot fucked up their html
			var pattern1 = PerformDiffAnalysis(withResultsDoc1, noResultBlacklist);
			var pattern2 = PerformDiffAnalysis(withResultsDoc2, noResultBlacklist);

			if (pattern1.Parent != pattern2.Parent)
			{
				throw new InvalidOperationException("Differential scraping found inconsistent patterns.");
			}
			var selector = WebService.GetCommonSelector(pattern1.Parent, pattern1.Elements.Concat(pattern2.Elements));
			return selector;
		}

		// Finds repeating element patterns that do NOT exist on the blacklist.
		private static RepeatingPattern PerformDiffAnalysis(IDocument withResultsDoc, HashSet<ElementSelector> blacklist)
		{
			// Filter elements to only those present in the 'diff' selector set.
			var candidateElements = withResultsDoc.All
					.Where(e =>
							{
								if (e.ParentElement == null) return false;

								var baseElementSelector = WebService.GetElementSelector(e);
								var fullParentPath = WebService.GetTagPath(withResultsDoc.DocumentElement, e.ParentElement);
								if (string.IsNullOrEmpty(fullParentPath)) return false;
								var fullSelector = new ElementSelector
								{
									Parent = fullParentPath,
									Element = baseElementSelector.Element
								};

								return !blacklist.Contains(fullSelector);
							})
					.ToList();
			var validPatterns = candidateElements
				.GroupBy(el => el.ParentElement) // Group siblings together
				.SelectMany(siblingGroup =>
					// Inside each sibling group, find elements with identical child structures.
					siblingGroup
						// Key: "DIVSPANIMG"
						.GroupBy(el => string.Join("", el.Children.Select(c => c.TagName)))
						// 3. Apply the final filtering criteria.
						.Where(patternGroup =>
						{
							var firstElementInGroup = patternGroup.FirstOrDefault();
							if (firstElementInGroup == null)
							{
								return false;
							}
							var isCardATag = firstElementInGroup.TagName.Equals("A", StringComparison.OrdinalIgnoreCase);
							var containsATag = patternGroup.Count() > 1 && // It must be a repeating pattern.
								   firstElementInGroup.Children.Length > 0 && // Must have children.
								   firstElementInGroup.QuerySelector("a") != null; // A descendant must be an <a> tag.
							return isCardATag || containsATag; // Must be or contain an <a> tag.
						})
						// 4. Project the valid groups into our record for scoring.
						.Select(validGroup =>
						{
							var elements = validGroup.ToList(); // Iterate ONCE to create the list
							return new RepeatingPattern
							{
								Parent = WebService.BuildParentSelector(siblingGroup.Key!),
								Elements = elements,           // Use the created list
								Count = elements.Count         // Use the list's .Count property (instant) };
							};
						})
				)
				.ToList();

			if (validPatterns.Count == 0)
			{
				throw new InvalidOperationException("No valid repeating patterns found.");
			}

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
			string[] paginationKeywords = ["next", "next page", "next results", "more results", "more", "prev", "previous", "last", "first", ">", "<", "»", "«"];
			return paginationKeywords.Any(key => text.Equals(key, StringComparison.OrdinalIgnoreCase));
		}

		// REGEX for extracting a four-digit year (19xx or 20xx)
		[GeneratedRegex(@"\b(19|20)\d{2}\b")]
		private static partial Regex YearRegex();
		// Extracts the first four-digit year (19xx or 20xx) found in the input string.
		public static int? ExtractYear(string content)
		{
			if (string.IsNullOrWhiteSpace(content)) return null;

			// Get the first match of the year regex
			var match = YearRegex().Match(content);

			// If a match is found and can be parsed as an int, return it. Otherwise, return null.
			if (match.Success && int.TryParse(match.Value, out int yearValue))
			{
				return yearValue;
			}

			return null;
		}
	}
}
