using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using spyglass_backend.Features.WebUtils;

namespace spyglass_backend.Features.Links
{
    public partial class SearchLinkService(ILogger<SearchLinkService> logger, IWebService webService)
    {
        private readonly ILogger<SearchLinkService> _logger = logger;
        private readonly IWebService _webService = webService;

        public async Task<SearchLink> ScrapeSearchLinksAsync(
            WebsiteLink link,
            bool useProxy = false
        )
        {
            _logger.LogInformation(
                "Scraping search links for {Url} (Proxy: {UseProxy})...",
                link.Url,
                useProxy
            );

            var (document, _) = await _webService.GetHtmlDocumentAsync(
                link.Url,
                useProxy: useProxy
            );

            // --- Phase 1 & 2: Find all potential search inputs globally on the page ---
            var allInputs = document.QuerySelectorAll("input[type='search'], input[type='text']");
            var candidateInputs = new List<IElement>();

            foreach (var input in allInputs)
            {
                var form = input.Closest("form");
                if (form != null)
                {
                    if (!IsLikelySearchForm(form)) continue;
                    var method = form.GetAttribute("method")?.ToLower() ?? "";
                    if (method.Length > 0 && method != "get") continue;
                }
                else
                {
                    var parentText = input.ParentElement?.TextContent ?? "";
                    if (NonSearchKeywordsRegex().IsMatch(parentText)) continue;
                }

                if (string.IsNullOrWhiteSpace(input.GetAttribute("name"))) continue;

                candidateInputs.Add(input);
            }

            IElement? bestInputSelection = null;
            if (candidateInputs.Count > 0)
            {
                try
                {
                    bestInputSelection = ChooseBestSearchInput(candidateInputs, link.Url);
                }
                catch (InvalidOperationException)
                {
                    // No suitable input scored > 0
                }
            }

            // --- Phase 3: If no valid search input found, probe common search URL patterns ---
            if (bestInputSelection == null)
            {
                // No HTML form/input found — probe common search URL patterns
                // (e.g., for SPA streaming sites with server-rendered search)
                var probePatterns = new[]
                {
                    "/search/{0}",
                    "/search?q={0}",
                    "/?s={0}",
                    "/search.php?q={0}",
                    "/index.php?s={0}",
                };

                var probeQueries = new[] { "batman", "mario", "sherlock", "naruto", "chrome" };

                var baseOrigin = new Uri(link.Url).GetLeftPart(UriPartial.Authority);

                foreach (var pattern in probePatterns)
                {
                    foreach (var probeQuery in probeQueries)
                    {
                        var formattedProbe = pattern.Replace("{0}", probeQuery);
                        var probeUrl = baseOrigin + formattedProbe;

                        try
                        {
                            var (probeDoc, _) = await _webService.GetHtmlDocumentAsync(
                                probeUrl,
                                useProxy: useProxy
                            );

                            if (probeDoc == null) continue;

                            var bodyText = probeDoc.Body?.TextContent ?? "";
                            if (bodyText.Contains(probeQuery, StringComparison.OrdinalIgnoreCase))
                            {
                                // Construct SearchUrl template using the pattern with {0} placeholder
                                var probeSearchUrl = baseOrigin + pattern;

                                _logger.LogInformation(
                                    "Found search link via probing for {Title}: {Url}",
                                    link.Title,
                                    probeSearchUrl
                                );

                                return new SearchLink
                                {
                                    Title = link.Title,
                                    Url = link.Url,
                                    Category = link.Category,
                                    Starred = link.Starred,
                                    SearchUrl = probeSearchUrl,
                                };
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            _logger.LogDebug(ex, "Probe failed for {ProbeUrl}", probeUrl);
                        }
                    }
                }

                throw new InvalidOperationException(
                    "No likely search forms, inputs, or working probe endpoints were found."
                );
            }

            // --- Phase 4: Construct the final SearchUrl template ---
            var inputName =
                bestInputSelection.GetAttribute("name")
                ?? throw new InvalidOperationException(
                    "The selected search input does not have a 'name' attribute."
                );

            var formElement = bestInputSelection.Closest("form");
            string searchUrlTemplate;
            if (formElement != null)
            {
                var actionUrl = formElement.GetAttribute("action") ?? "";
                var absoluteActionUri = new Uri(new Uri(link.Url), actionUrl);
                var uriBuilder = new UriBuilder(absoluteActionUri)
                {
                    Query = $"{Uri.EscapeDataString(inputName)}={{0}}",
                };
                searchUrlTemplate = uriBuilder.ToString();
            }
            else
            {
                // Formless input: submit query parameter directly to base origin
                var baseUri = new Uri(link.Url);
                var uriBuilder = new UriBuilder(baseUri)
                {
                    Query = $"{Uri.EscapeDataString(inputName)}={{0}}"
                };
                searchUrlTemplate = uriBuilder.ToString();
            }

            _logger.LogInformation(
                "Found search link for {Title}: {Url}",
                link.Title,
                searchUrlTemplate
            );

            return new SearchLink
            {
                Title = link.Title,
                Url = link.Url,
                Category = link.Category,
                Starred = link.Starred,
                SearchUrl = searchUrlTemplate,
            };
        }

        private static readonly string[] SearchAttributes =
        [
            "id",
            "name",
            "aria-label",
            "data-testid",
        ];

        [GeneratedRegex(
            @"login|log in|sign ?in|username|password|register|sign ?up|subscribe|newsletter|contact|comment|forgot|e-mail|email",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        )]
        private static partial Regex NonSearchKeywordsRegex();

        [GeneratedRegex(@"search|magnify|loupe", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
        private static partial Regex SearchIconRegex();

        // A private record to hold scoring data during the search input selection process.
        private record SearchCandidate(int Score, IElement Selection, string Reasoning);

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
            foreach (
                var el in formSelection.QuerySelectorAll(
                    "h1, h2, h3, button, a[role='button'], input[type='submit']"
                )
            )
            {
                formTextBuilder.Append(el.TextContent).Append(' ');
            }

            // C# Regex.IsMatch is the equivalent of Go's nonSearchKeywords.MatchString.
            return !NonSearchKeywordsRegex().IsMatch(formTextBuilder.ToString());
        }

        // This method finds the best search input element from a list of candidates.
        private static IElement ChooseBestSearchInput(
            IEnumerable<IElement> candidates,
            string sourceUrl
        )
        {
            if (!candidates.Any())
            {
                // We throw a custom exception, which is idiomatic C# error handling.
                throw new InvalidOperationException(
                    $"No valid form with a single search input was found on: {sourceUrl}"
                );
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
                    var reasons = new List<string>
                    {
                        score > 10 ? "+100 (Base:type='search')" : "+10 (Base:type='text')",
                    };
                    var positiveSignals = 0;

                    // --- Scoring Logic ---
                    var form = sel.Closest("form");

                    // Check for role="search" ancestor
                    if (sel.Closest("[role='search']") != null)
                    {
                        score += 75;
                        reasons.Add("+75 (in role='search')");
                        positiveSignals++;
                    }
                    // Check for header or nav ancestor
                    if (sel.Closest("header") != null)
                    {
                        score += 50;
                        reasons.Add("+50 (in <header>)");
                        positiveSignals++;
                    }
                    else if (sel.Closest("nav") != null)
                    {
                        score += 40;
                        reasons.Add("+40 (in <nav>)");
                        positiveSignals++;
                    }

                    // Check attributes (id, name, aria-label, data-testid) for search keywords
                    foreach (var attr in SearchAttributes)
                    {
                        if (sel.HasAttribute(attr))
                        {
                            var val = sel.GetAttribute(attr)?.ToLower();
                            if (
                                val != null
                                && (
                                    val.Contains("search")
                                    || val == "q"
                                    || val == "s"
                                    || val == "query"
                                )
                            )
                            {
                                score += 35;
                                reasons.Add($"+35 (attr {attr})");
                                positiveSignals++;
                            }
                        }
                    }

                    // Check placeholder attribute for "search"
                    if (
                        sel.GetAttribute("placeholder")
                            ?.Contains("search", StringComparison.OrdinalIgnoreCase)
                        ?? false
                    )
                    {
                        score += 20;
                        reasons.Add("+20 (placeholder)");
                    }

                    // Check adjacent buttons or links (limited to the form)
                    foreach (
                        var btn in form?.QuerySelectorAll("button, a[role='button']")
                            ?? Enumerable.Empty<IElement>()
                    )
                    {
                        if (
                            btn.TextContent.Contains("search", StringComparison.OrdinalIgnoreCase)
                            || (
                                btn.HasAttribute("class")
                                && SearchIconRegex().IsMatch(btn.GetAttribute("class") ?? "")
                            )
                        )
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
                        score -= 200;
                        reasons.Add("-200 (in <footer>)");
                    }
                    if (sel.Closest("aside, .sidebar") != null)
                    {
                        score -= 100;
                        reasons.Add("-100 (in sidebar)");
                    }

                    // Certainty bonus for multiple strong signals
                    if (positiveSignals >= 3)
                    {
                        score += 50;
                        reasons.Add("+50 (Certainty Bonus)");
                    }

                    // Return the candidate as our private record
                    return new SearchCandidate(score, sel, string.Join(", ", reasons));
                })
                .Where(c => c.Score > 0) // Filter out candidates with scores <= 0
                .OrderByDescending(c => c.Score) // Sort by score, highest first
                .ToList(); // Execute the query

            var validCandidates = scoredCandidates
                .Where(c =>
                    c.Selection.HasAttribute("name")
                    && !string.IsNullOrWhiteSpace(c.Selection.GetAttribute("name"))
                )
                .ToList();

            // --- New Selection Logic on the Filtered List ---
            if (validCandidates.Count == 0)
            {
                // If, after scoring, none of the potential candidates have a 'name' attribute, we must fail.
                throw new InvalidOperationException(
                    $"No suitable search input with a 'name' attribute could be found on: {sourceUrl}"
                );
            }

            // If only one valid candidate remains, it's our winner.
            if (validCandidates.Count == 1)
            {
                return validCandidates[0].Selection;
            }

            // Return the highest-scoring valid candidate.
            return validCandidates[0].Selection;
        }
    }
}

