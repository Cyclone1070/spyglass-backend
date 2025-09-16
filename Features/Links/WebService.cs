using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;

using AngleSharp.Dom;

namespace spyglass_backend.Features.Links
{
	public partial class WebService(
		IHttpClientFactory httpClientFactory)
	{
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

		public async Task<(IDocument, long)> GetHtmlDocumentAsync(string url)
		{
			var client = _httpClientFactory.CreateClient();
			var stopwatch = Stopwatch.StartNew();
			var htmlContent = await client.GetStringAsync(url);
			stopwatch.Stop();
			var context = BrowsingContext.New(AngleSharp.Configuration.Default);
			var document = await context.OpenAsync(req => req.Content(htmlContent));
			return (document, stopwatch.ElapsedMilliseconds);
		}

		public static ElementSelector GetElementSelector(IElement element)
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
		public static ElementSelector GetCommonSelector(string parent, IEnumerable<IElement> elements)
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
		public static string BuildClassSelector(IElement element)
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
		public static string BuildParentSelector(IElement element)
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

		public static string? GetTagPath(IElement? parent, IElement? child)
		{
			if (parent == null || child == null)
			{
				throw new InvalidOperationException("Parent and child elements cannot be null.");
			}
			// If the child is the parent, there's no path.
			if (parent == child)
			{
				return null;
			}

			var pathSegments = new List<string>();
			var currentNode = child;

			// Walk up the tree from the child until we hit the parent.
			// The loop must also check for null in case the child is not a descendant of the parent.
			while (currentNode != null && currentNode != parent)
			{
				// Prepend the tag name to our list of segments.
				pathSegments.Add(currentNode.TagName.ToLowerInvariant());
				currentNode = currentNode.ParentElement;
			}

			// After the loop, if currentNode is not the parent, it means we reached the top of the document
			// without finding the parent, so the child is not a descendant.
			if (currentNode != parent)
			{
				throw new InvalidOperationException("The specified child is not a descendant of the specified parent.");
			}

			// The path was built from child-to-parent, so we need to reverse it.
			pathSegments.Reverse();

			// Join the segments with the direct child combinator.
			return string.Join(" > ", pathSegments);
		}
	}
}
