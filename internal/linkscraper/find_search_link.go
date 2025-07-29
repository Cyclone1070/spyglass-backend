// Package linkscraper provides functionality to scrape and discover website links.
// It includes tools for extracting categorized links from curated lists (like those on fmhy.net)
// and for automatically finding a website's search functionality.
package linkscraper

import (
	"errors"
	"fmt"
	"net/url"
	"regexp"
	"sort"
	"strings"

	"github.com/Cyclone1070/spyglass-backend/internal/utils"
	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

// SearchLink extends a WebsiteLink with the specific URL template for
// that site's search functionality. This is the final output of the
// discovery process.
type SearchLink struct {
	WebsiteLink
	// SearchURL is a printf-style format string for the site's search endpoint.
	// It contains a single "%s" which should be replaced with the URL-encoded query.
	// Example: "https://example.com/search?q=%s"
	SearchURL string
}

// FindSearchLink discovers the direct GET request URL for a website's search functionality.
// It operates in several phases:
// 1. Fetches the page content using a configured gocolly collector.
// 2. Filters all `<form>` elements to find ones that are likely for search.
// 3. Narrows the selection to forms using the GET method.
// 4. Further filters for forms containing exactly one text or search input field.
// 5. Uses a sophisticated scoring engine (`chooseBestSearchInput`) to select the best candidate.
// 6. Constructs the final search URL template, preserving any existing query parameters.
// This entire process is browser-free and relies on static analysis of the HTML.
func FindSearchLink(link WebsiteLink) (SearchLink, error) {
	c := utils.ConfiguredCollector()

	var result SearchLink
	var scrapeErr error

	// OnError is called if an error occurs during the request.
	c.OnError(func(r *colly.Response, err error) {
		scrapeErr = fmt.Errorf("request to %s failed with status %d: %w", r.Request.URL, r.StatusCode, err)
	})

	// OnHTML is where all the parsing and discovery logic happens.
	c.OnHTML("html", func(e *colly.HTMLElement) {
		// --- Phase 1: Filter for likely search forms ---
		searchForms := e.DOM.Find("form").FilterFunction(isLikelySearchForm)

		// --- Phase 2: Apply the "GET request only" constraint ---
		getForms := searchForms.FilterFunction(func(i int, s *goquery.Selection) bool {
			method, _ := s.Attr("method")
			// An absent method attribute defaults to "GET", so we include it.
			return method == "" || strings.ToLower(method) == "get"
		})

		if getForms.Length() == 0 {
			scrapeErr = errors.New("no likely search forms with method=GET were found")
			return
		}

		// --- Phase 3: Find forms with exactly one valid input ---
		var validSingleInputs []*goquery.Selection
		getForms.Each(func(i int, formSelection *goquery.Selection) {
			inputsInThisForm := formSelection.Find("input[type='search'], input[type='text']")
			if inputsInThisForm.Length() == 1 {
				validSingleInputs = append(validSingleInputs, inputsInThisForm)
			}
		})

		// --- Phase 4: Use the scoring engine to choose the single best candidate ---
		selection, err := chooseBestSearchInput(validSingleInputs, link.URL)
		if err != nil {
			scrapeErr = err // Propagate the error from the helper.
			return
		}

		// --- Phase 5: Construct the final SearchURL template from the winning form ---
		form := selection.Closest("form")
		inputName, nameExists := selection.Attr("name")
		if !nameExists || inputName == "" {
			scrapeErr = errors.New("winning search input has no 'name' attribute")
			return
		}

		actionURL, _ := form.Attr("action")

		// Resolve the action URL to be absolute, using the request URL as the base.
		absoluteActionURL, err := e.Request.URL.Parse(actionURL)
		if err != nil {
			scrapeErr = fmt.Errorf("could not parse form action URL '%s': %w", actionURL, err)
			return
		}

		// Build the final URL template. e.g., "https://site.com/search?q=%s"
		// We use url.Values to correctly handle existing query params in the action.
		queryValues, err := url.ParseQuery(absoluteActionURL.RawQuery)
		if err != nil {
			scrapeErr = fmt.Errorf("could not parse query from action URL: %w", err)
			return
		}
		queryValues.Set(inputName, "QUERY_PLACEHOLDER")

		// Encode the values. This correctly handles all other parameters.
		// The result is like: "lang=en&q=QUERY_PLACEHOLDER"
		encodedQuery := queryValues.Encode()

		// Now, manually replace the safe placeholder with the real %s AFTER encoding.
		// The '%' is no longer seen by the encoder and will not be escaped.
		finalQueryTemplate := strings.Replace(encodedQuery, "QUERY_PLACEHOLDER", "%s", 1)

		// Rebuild the final URL template.
		baseURL := absoluteActionURL.Scheme + "://" + absoluteActionURL.Host + absoluteActionURL.Path
		searchURLTemplate := fmt.Sprintf("%s?%s", baseURL, finalQueryTemplate)

		result = SearchLink{
			WebsiteLink: link,
			SearchURL:   searchURLTemplate,
		}
	})

	// Start the collector. This is a blocking call.
	c.Visit(link.URL)

	// After visiting, check for errors set in the callbacks.
	if scrapeErr != nil {
		return SearchLink{}, scrapeErr
	}
	if result.SearchURL == "" {
		return SearchLink{}, errors.New("discovery complete, but no valid search URL was found")
	}

	return result, nil
}

// nonSearchKeywords is a regex used to filter out forms that are clearly not
// for site search, such as login, registration, or newsletter signup forms.
var nonSearchKeywords = regexp.MustCompile(
	`login|log in|sign ?in|username|password|register|sign ?up|subscribe|newsletter|contact|comment|forgot|e-mail|email`,
)

// isLikelySearchForm is a goquery filter function that returns true if a form
// is a potential candidate for being a search form. It filters out forms with
// password fields or textareas and checks surrounding text for non-search keywords.
func isLikelySearchForm(i int, s *goquery.Selection) bool {
	// Rule 1: A search form should not contain password fields or textareas.
	if s.Find(`input[type="password"], textarea`).Length() > 0 {
		return false
	}
	// Rule 2: Check the text content of headings and buttons within the form.
	// If it contains keywords related to login, subscribe, etc., it's not a search form.
	var formTextBuilder strings.Builder
	s.Find("h1, h2, h3, button, a[role='button'], input[type='submit']").Each(func(_ int, el *goquery.Selection) {
		formTextBuilder.WriteString(el.Text())
		formTextBuilder.WriteString(" ")
	})
	return !nonSearchKeywords.MatchString(strings.ToLower(formTextBuilder.String()))
}

// chooseBestSearchInput implements the scoring engine to select the most likely
// search input from a list of candidates. It assigns scores based on a variety
// of weighted signals, such as HTML attributes (type, role, name), location in
// the DOM (header, nav), and associated button text or icons.
func chooseBestSearchInput(candidates []*goquery.Selection, sourceURL string) (*goquery.Selection, error) {
	if len(candidates) == 0 {
		return nil, fmt.Errorf("no valid form with a single search input was found on: %s", sourceURL)
	}
	if len(candidates) == 1 {
		return candidates[0], nil // No need to score if there's only one choice.
	}

	type searchCandidate struct {
		score     int
		selection *goquery.Selection
		reasoning string
	}
	searchIconRegex := regexp.MustCompile(`search|magnify|loupe`)
	scoredCandidates := make([]searchCandidate, len(candidates))
	for i, sel := range candidates {
		scoredCandidates[i] = searchCandidate{selection: sel}
	}
	for i := range scoredCandidates {
		sel := scoredCandidates[i].selection
		form := sel.Closest("form")
		var reasons []string
		var positiveSignals int
		// --- Scoring Logic ---
		// Positive signals
		if inputType, _ := sel.Attr("type"); inputType == "search" {
			scoredCandidates[i].score += 100
			reasons = append(reasons, "+100 (Base:type='search')")
		} else {
			scoredCandidates[i].score += 10
			reasons = append(reasons, "+10 (Base:type='text')")
		}
		if sel.Closest(`[role="search"]`).Length() > 0 {
			scoredCandidates[i].score += 75
			reasons = append(reasons, "+75 (in role='search')")
			positiveSignals++
		}
		if sel.Closest("header").Length() > 0 {
			scoredCandidates[i].score += 50
			reasons = append(reasons, "+50 (in <header>)")
			positiveSignals++
		} else if sel.Closest("nav").Length() > 0 {
			scoredCandidates[i].score += 40
			reasons = append(reasons, "+40 (in <nav>)")
			positiveSignals++
		}
		for _, attr := range []string{"id", "name", "aria-label", "data-testid"} {
			if val, ok := sel.Attr(attr); ok {
				val = strings.ToLower(val)
				if strings.Contains(val, "search") || val == "q" || val == "s" || val == "query" {
					scoredCandidates[i].score += 35
					reasons = append(reasons, fmt.Sprintf("+35 (attr %s)", attr))
					positiveSignals++
				}
			}
		}
		if placeholder, ok := sel.Attr("placeholder"); ok && strings.Contains(strings.ToLower(placeholder), "search") {
			scoredCandidates[i].score += 20
			reasons = append(reasons, "+20 (placeholder)")
		}
		form.Find("button, a[role='button']").EachWithBreak(func(_ int, btn *goquery.Selection) bool {
			if strings.Contains(strings.ToLower(btn.Text()), "search") {
				scoredCandidates[i].score += 50
				reasons = append(reasons, "+50 (adj. btn text)")
				positiveSignals++
				return false // Stop searching once found.
			}
			if class, ok := btn.Attr("class"); ok && searchIconRegex.MatchString(class) {
				scoredCandidates[i].score += 50
				reasons = append(reasons, "+50 (adj. btn icon)")
				positiveSignals++
				return false // Stop searching once found.
			}
			return true
		})
		// Negative signals
		if sel.Closest("footer").Length() > 0 {
			scoredCandidates[i].score -= 200
			reasons = append(reasons, "-200 (in <footer>)")
		}
		if sel.Closest("aside, .sidebar").Length() > 0 {
			scoredCandidates[i].score -= 100
			reasons = append(reasons, "-100 (in sidebar)")
		}
		// Certainty bonus for multiple strong signals
		if positiveSignals >= 3 {
			scoredCandidates[i].score += 50
			reasons = append(reasons, "+50 (Certainty Bonus)")
		}
		scoredCandidates[i].reasoning = strings.Join(reasons, ", ")
	}
	// --- Selection Logic ---
	var viableCandidates []searchCandidate
	for _, c := range scoredCandidates {
		if c.score > 0 {
			viableCandidates = append(viableCandidates, c)
		}
	}
	if len(viableCandidates) == 0 {
		return nil, fmt.Errorf("multiple inputs found, but none could be confidently identified on: %s", sourceURL)
	}
	if len(viableCandidates) == 1 {
		return viableCandidates[0].selection, nil
	}
	sort.Slice(viableCandidates, func(i, j int) bool {
		return viableCandidates[i].score > viableCandidates[j].score
	})
	// Check for ambiguity: if the top two scores are too close, it's safer to fail.
	if (viableCandidates[0].score - viableCandidates[1].score) < 20 {
		return nil, fmt.Errorf("multiple inputs have very close scores (Top: %d, Next: %d), unable to resolve ambiguity on: %s", viableCandidates[0].score, viableCandidates[1].score, sourceURL)
	}
	return viableCandidates[0].selection, nil
}
