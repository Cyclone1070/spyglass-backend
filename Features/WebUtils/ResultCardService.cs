using AngleSharp.Dom;
using spyglass_backend.Features.Links;

namespace spyglass_backend.Features.WebUtils
{
    public static class ResultCardService
    {
        public static bool IsNoResultsPage(IDocument document)
        {
            var text = document.Body?.TextContent ?? "";
            string[] negativeKeywords =
            [
                "no results",
                "0 results",
                "no matches",
                "not found",
                "nothing found",
                "0 matches",
                "try another",
                "no files",
                "no torrents",
                "empty",
                "no search results",
                "no movies",
                "no video"
            ];
            return negativeKeywords.Any(key => text.Contains(key, StringComparison.OrdinalIgnoreCase));
        }

        public static ElementSelector FindResultCardSelector(
            HashSet<ElementSelector> noResultBlacklist,
            IDocument withResultsDoc1,
            IDocument withResultsDoc2,
            string? query1 = null,
            string? query2 = null
        )
        {
            // Find the repeating pattern, do it twice to get a wider variety of cards in case some idiot fucked up their html
            var pattern1 = PerformDiffAnalysis(withResultsDoc1, noResultBlacklist, query1);
            var pattern2 = PerformDiffAnalysis(withResultsDoc2, noResultBlacklist, query2);

            if (pattern1.Parent != pattern2.Parent)
            {
                throw new InvalidOperationException(
                    "Differential scraping found inconsistent patterns."
                );
            }
            return WebService.GetCommonSelector(
                pattern1.Parent,
                pattern1.Elements.Concat(pattern2.Elements)
            );
        }

        // Finds repeating element patterns that do NOT exist on the blacklist.
        private static RepeatingPattern PerformDiffAnalysis(
            IDocument withResultsDoc,
            HashSet<ElementSelector> blacklist,
            string? query
        )
        {
            // Filter elements to only those present in the 'diff' selector set.
            var candidateElements = withResultsDoc
                .All.Where(e =>
                {
                    if (e.ParentElement == null)
                        return false;

                    var baseElementSelector = WebService.GetElementSelector(e);
                    var fullParentPath = WebService.GetTagPath(
                        withResultsDoc.DocumentElement,
                        e.ParentElement
                    );
                    if (string.IsNullOrEmpty(fullParentPath))
                        return false;
                    var fullSelector = new ElementSelector
                    {
                        Parent = fullParentPath,
                        Element = baseElementSelector.Element,
                    };

                    if (IsLayoutElement(e))
                        return false;

                    return !blacklist.Contains(fullSelector);
                })
                .ToList();
            var validPatterns = candidateElements
                .GroupBy(el => el.ParentElement) // Group siblings together
                .SelectMany(siblingGroup =>
                    // Inside each sibling group, find elements with identical child structures.
                    siblingGroup
                        // Key: "DIVSPANIMG"
                        .GroupBy(el => string.Concat(el.Children.Select(c => c.TagName)))
                        // 3. Apply the final filtering criteria.
                        .Where(patternGroup =>
                        {
                            var firstElementInGroup = patternGroup.FirstOrDefault();
                            if (firstElementInGroup == null)
                            {
                                return false;
                            }
                            var isCardNavigable = IsNavigable(firstElementInGroup);
                            var containsNavigable =
                                patternGroup.Count() > 1
                                && (firstElementInGroup.Children.Length > 0)
                                && ContainsNavigable(firstElementInGroup);
                            return isCardNavigable || containsNavigable;
                        })
                        // 4. Project the valid groups into our record for scoring.
                        .Select(validGroup =>
                        {
                            var elements = validGroup.ToList(); // Iterate ONCE to create the list
                            return new RepeatingPattern
                            {
                                Parent = WebService.BuildParentSelector(siblingGroup.Key!),
                                Elements = elements, // Use the created list
                                Count = elements.Count, // Use the list's .Count property (instant) };
                            };
                        })
                )
                .ToList();

            if (!string.IsNullOrEmpty(query))
            {
                var normalizedQuery = query.ToLowerInvariant();
                var filtered = validPatterns
                    .Where(p => p.Elements.Any(el =>
                    {
                        var text = el.TextContent.ToLowerInvariant();
                        var href = el.QuerySelector("a")?.GetAttribute("href")?.ToLowerInvariant() ?? "";
                        var cardHref = el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase)
                            ? el.GetAttribute("href")?.ToLowerInvariant() ?? ""
                            : "";
                        return text.Contains(normalizedQuery) || href.Contains(normalizedQuery) || cardHref.Contains(normalizedQuery);
                    }))
                    .ToList();

                if (filtered.Count > 0)
                {
                    validPatterns = filtered;
                }
            }

            if (validPatterns.Count == 0)
            {
                throw new InvalidOperationException("No valid repeating patterns found.");
            }

            return validPatterns
                .Where(p => !p.Elements.Any(IsPaginationCard))
                .Select(p => new
                {
                    Pattern = p,
                    Score = (p.Count * 10) + CalculateComplexityScore(p.Elements[0]),
                })
                .OrderByDescending(x => x.Score)
                .First()
                .Pattern;
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
            string[] paginationKeywords =
            [
                "next",
                "next page",
                "next results",
                "more results",
                "more",
                "prev",
                "previous",
                "last",
                "first",
                ">",
                "<",
                "»",
                "«",
            ];
            return paginationKeywords.Any(key =>
                text.Equals(key, StringComparison.OrdinalIgnoreCase)
            );
        }

        public static string? GetImageUrlFromElement(string baseUrl, IElement? imgElement)
        {
            if (imgElement == null)
                return null;

            // Prioritized list of attributes to check for an image URL.
            foreach (var attrName in (string[])["data-src", "src", "data-original", "data-image"])
            {
                string? imgUrl = imgElement.GetAttribute(attrName);

                if (!string.IsNullOrWhiteSpace(imgUrl))
                {
                    return ResultATagService.ToAbsoluteUrl(baseUrl, imgUrl);
                }
            }

            return null; // No valid image URL found in any of the specified attributes.
        }

        private static bool IsLayoutElement(IElement element)
        {
            var current = element;
            while (current != null)
            {
                var tagName = current.TagName.ToLowerInvariant();
                if (tagName == "nav" || tagName == "header" || tagName == "footer" || tagName == "aside")
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(current.ClassName))
                {
                    var className = current.ClassName.ToLowerInvariant();
                    if (className.Contains("menu") || 
                        className.Contains("dropdown") || 
                        className.Contains("nav") || 
                        className.Contains("sidebar") || 
                        className.Contains("filter") || 
                        className.Contains("widget") || 
                        className.Contains("popup"))
                    {
                        return true;
                    }
                }
                current = current.ParentElement;
            }
            return false;
        }

        private static bool IsNavigable(IElement el)
        {
            if (el.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                return true;

            string[] navAttrs = ["onclick", "data-href", "data-link", "data-url"];
            if (navAttrs.Any(attr => el.HasAttribute(attr) && !string.IsNullOrWhiteSpace(el.GetAttribute(attr))))
                return true;

            if (el.GetAttribute("role")?.ToLowerInvariant() == "link")
                return true;

            return false;
        }

        private static bool ContainsNavigable(IElement el)
        {
            if (el.QuerySelector("a") != null)
                return true;

            var descendants = el.QuerySelectorAll("*");
            foreach (var d in descendants)
            {
                if (IsNavigable(d))
                    return true;
            }

            return false;
        }
    }
}
