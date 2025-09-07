using AngleSharp.Dom;

using AngleSharp;
using System.Text;
using System.Text.RegularExpressions;

namespace spyglass_backend.Features.Links
{
	public partial class SearchLinkService(
		ILogger<SearchLinkService> logger,
		IHttpClientFactory httpClientFactory)
	{
		private readonly ILogger<SearchLinkService> _logger = logger;
		private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

		public async Task<SearchLink> ScrapeSearchLinksAsync(WebsiteLink link)
		{
			_logger.LogInformation("Scraping search links for {Url}...", link.Url);

			var client = _httpClientFactory.CreateClient();
			var htmlContent = await client.GetStringAsync(link.Url);

			var context = BrowsingContext.New(AngleSharp.Configuration.Default);
			var document = await context.OpenAsync(req => req.Content(htmlContent));

			// --- Phase 1: Filter for likely search forms ---
			// AngleSharp's Filter() is a LINQ extension method for filtering
			var searchForms = document.QuerySelectorAll("form").Where(IsLikelySearchForm);

			// --- Phase 2: Apply the "GET request only" constraint ---
			var getForms = searchForms.Where(s =>
			{
				var method = s.GetAttribute("method")?.ToLower() ?? "";
				return method == "" || method == "get";
			}).ToList();

			if (getForms.Count == 0)
			{
				throw new InvalidOperationException("No likely search forms with method=GET were found.");
			}

			// --- Phase 3: Find forms with exactly one valid input ---
			var validSingleInputs = new List<IElement>();
			foreach (var formSelection in getForms)
			{
				var inputsInThisForm = formSelection.QuerySelectorAll("input[type='search'], input[type='text']");
				if (inputsInThisForm.Length == 1)
				{
					validSingleInputs.Add(inputsInThisForm.First());
				}
			}

			// --- Phase 4: Use the scoring engine to choose the single best candidate ---
			var bestInputSelection = ChooseBestSearchInput(validSingleInputs, link.Url);

			// --- Phase 5: Construct the final SearchUrl template ---
			var form = bestInputSelection.Closest("form")
				?? throw new InvalidOperationException("The selected search input is not contained within a <form> element.");
			var inputName = bestInputSelection.GetAttribute("name")
				?? throw new InvalidOperationException("The selected search input does not have a 'name' attribute.");

			var actionUrl = form.GetAttribute("action") ?? "";

			// Use UriBuilder for idiomatic C# Url manipulation, ensuring absolute Urls.
			var absoluteActionUri = new Uri(new Uri(link.Url), actionUrl);
			var uriBuilder = new UriBuilder(absoluteActionUri)
			{
				// This creates a query string template like "q={0}"
				Query = $"{Uri.EscapeDataString(inputName)}={{0}}"
			};
			var searchUrlTemplate = uriBuilder.ToString();

			_logger.LogInformation("Found search link for {Title}: {Url}", link.Title, searchUrlTemplate);

			return new SearchLink
			{
				Title = link.Title,
				Url = link.Url,
				Category = link.Category,
				Starred = link.Starred,
				SearchUrl = searchUrlTemplate
			};
		}


		private static readonly string[] SearchAttributes = ["id", "name", "aria-label", "data-testid"];

		[GeneratedRegex(
			@"login|log in|sign ?in|username|password|register|sign ?up|subscribe|newsletter|contact|comment|forgot|e-mail|email",
			RegexOptions.IgnoreCase | RegexOptions.Compiled)]
		private static partial Regex NonSearchKeywordsRegex();

		[GeneratedRegex(
			@"search|magnify|loupe",
			RegexOptions.IgnoreCase | RegexOptions.Compiled)]
		private static partial Regex SearchIconRegex();

		// A private record to hold scoring data during the search input selection process.
		private record SearchCandidate(
			int Score,
			IElement Selection,
			string Reasoning
		);

		// This method checks if a form is likely a search form based on its content.
		private static bool IsLikelySearchForm(IElement formSelection)
		{
			// Rule 1: A search form should not contain password fields or textareas.
			// AngleSharp's QuerySelectorAll is the equivalent of goquery's Find.
			if (formSelection.QuerySelectorAll("input[type='password'], textarea").Length > 0)
			{
				return false;
			}

			// Rule 2: Check the text content of headings and buttons within the form for non-search keywords.
			var formTextBuilder = new StringBuilder();

			// Iterate over elements like h1, h2, etc., and append their text to the builder.
			formSelection.QuerySelectorAll("h1, h2, h3, button, a[role='button'], input[type='submit']")
				.ToList()
				.ForEach(el => formTextBuilder.Append(el.TextContent).Append(' '));

			// C# Regex.IsMatch is the equivalent of Go's nonSearchKeywords.MatchString.
			return !NonSearchKeywordsRegex().IsMatch(formTextBuilder.ToString());
		}

		// This method finds the best search input element from a list of candidates.
		private static IElement ChooseBestSearchInput(IEnumerable<IElement> candidates, string sourceUrl)
		{
			if (!candidates.Any())
			{
				// We throw a custom exception, which is idiomatic C# error handling.
				throw new InvalidOperationException($"No valid form with a single search input was found on: {sourceUrl}");
			}

			if (candidates.Count() == 1)
			{
				return candidates.First();
			}

			var scoredCandidates = candidates
				.Select(sel =>
				{
					// Initial score based on input type (type='search' is better than type='text')
					var score = sel.GetAttribute("type")?.ToLower() == "search" ? 100 : 10;
					var reasons = new List<string> { score > 10 ? "+100 (Base:type='search')" : "+10 (Base:type='text')" };
					var positiveSignals = 0;

					// --- Scoring Logic ---
					var form = sel.Closest("form");

					// Check for role="search" ancestor
					if (sel.Closest("[role='search']") != null)
					{
						score += 75; reasons.Add("+75 (in role='search')"); positiveSignals++;
					}
					// Check for header or nav ancestor
					if (sel.Closest("header") != null)
					{
						score += 50; reasons.Add("+50 (in <header>)"); positiveSignals++;
					}
					else if (sel.Closest("nav") != null)
					{
						score += 40; reasons.Add("+40 (in <nav>)"); positiveSignals++;
					}

					// Check attributes (id, name, aria-label, data-testid) for search keywords
					foreach (var attr in SearchAttributes)
					{
						if (sel.HasAttribute(attr))
						{
							var val = sel.GetAttribute(attr)?.ToLower();
							if (val != null && (val.Contains("search") || val == "q" || val == "s" || val == "query"))
							{
								score += 35; reasons.Add($"+35 (attr {attr})"); positiveSignals++;
							}
						}
					}

					// Check placeholder attribute for "search"
					if (sel.GetAttribute("placeholder")?.Contains("search", StringComparison.OrdinalIgnoreCase) ?? false)
					{
						score += 20; reasons.Add("+20 (placeholder)");
					}

					// Check adjacent buttons or links (limited to the form)
					foreach (var btn in form?.QuerySelectorAll("button, a[role='button']") ?? Enumerable.Empty<IElement>())
					{
						if (btn.TextContent.Contains("search", StringComparison.OrdinalIgnoreCase) ||
							(btn.HasAttribute("class") && SearchIconRegex().IsMatch(btn.GetAttribute("class") ?? "")))
						{
							score += 50;
							reasons.Add("+50 (adj. btn match)");
							positiveSignals++;
							break; // Similar to Go's EachWithBreak, we stop when we find a match
						}
					}

					// Negative signals
					if (sel.Closest("footer") != null)
					{
						score -= 200; reasons.Add("-200 (in <footer>)");
					}
					if (sel.Closest("aside, .sidebar") != null)
					{
						score -= 100; reasons.Add("-100 (in sidebar)");
					}

					// Certainty bonus for multiple strong signals
					if (positiveSignals >= 3)
					{
						score += 50; reasons.Add("+50 (Certainty Bonus)");
					}

					// Return the candidate as our private record
					return new SearchCandidate(score, sel, string.Join(", ", reasons));
				})
				.Where(c => c.Score > 0) // Filter out candidates with scores <= 0
				.OrderByDescending(c => c.Score) // Sort by score, highest first
				.ToList(); // Execute the query

			var validCandidates = scoredCandidates
					.Where(c => c.Selection.HasAttribute("name") && !string.IsNullOrWhiteSpace(c.Selection.GetAttribute("name")))
					.ToList();

			// --- New Selection Logic on the Filtered List ---
			if (validCandidates.Count == 0)
			{
				// If, after scoring, none of the potential candidates have a 'name' attribute, we must fail.
				throw new InvalidOperationException($"No suitable search input with a 'name' attribute could be found on: {sourceUrl}");
			}

			// If only one valid candidate remains, it's our winner.
			if (validCandidates.Count == 1)
			{
				return validCandidates.First().Selection;
			}

			// Return the highest-scoring valid candidate.
			return validCandidates[0].Selection;
		}
	}
}