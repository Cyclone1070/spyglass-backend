// Package linkscraper provides functionality to find search URL patterns on websites.
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

// --- Primary Data Structures ---

type SearchLink struct {
	WebsiteLink
	SearchURL string // The final URL template, e.g., "https://site.com/search?q=%s"
}

// --- Browser-Free Finder Function ---

// FindSearchLink discovers the direct GET request URL for a website's search functionality.
// It uses gocolly for a single HTTP request and reuses the scoring logic to find the best form.
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

		// --- Phase 2: Apply the new "GET request only" constraint ---
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

// --- Helper Functions (Unchanged from previous version) ---

var nonSearchKeywords = regexp.MustCompile(
	`login|log in|sign ?in|username|password|register|sign ?up|subscribe|newsletter|contact|comment|forgot|e-mail|email`,
)

func isLikelySearchForm(i int, s *goquery.Selection) bool {
	if s.Find(`input[type="password"], textarea`).Length() > 0 {
		return false
	}
	var formTextBuilder strings.Builder
	s.Find("h1, h2, h3, button, a[role='button'], input[type='submit']").Each(func(_ int, el *goquery.Selection) {
		formTextBuilder.WriteString(el.Text())
		formTextBuilder.WriteString(" ")
	})
	return !nonSearchKeywords.MatchString(strings.ToLower(formTextBuilder.String()))
}

func chooseBestSearchInput(candidates []*goquery.Selection, sourceURL string) (*goquery.Selection, error) {
	if len(candidates) == 0 {
		return nil, fmt.Errorf("no valid form with a single search input was found on: %s", sourceURL)
	}
	if len(candidates) == 1 {
		return candidates[0], nil
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
				return false
			}
			if class, ok := btn.Attr("class"); ok && searchIconRegex.MatchString(class) {
				scoredCandidates[i].score += 50
				reasons = append(reasons, "+50 (adj. btn icon)")
				positiveSignals++
				return false
			}
			return true
		})
		if sel.Closest("footer").Length() > 0 {
			scoredCandidates[i].score -= 200
			reasons = append(reasons, "-200 (in <footer>)")
		}
		if sel.Closest("aside, .sidebar").Length() > 0 {
			scoredCandidates[i].score -= 100
			reasons = append(reasons, "-100 (in sidebar)")
		}
		if positiveSignals >= 3 {
			scoredCandidates[i].score += 50
			reasons = append(reasons, "+50 (Certainty Bonus)")
		}
		scoredCandidates[i].reasoning = strings.Join(reasons, ", ")
	}
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
	if (viableCandidates[0].score - viableCandidates[1].score) < 20 {
		return nil, fmt.Errorf("multiple inputs have very close scores (Top: %d, Next: %d), unable to resolve ambiguity on: %s", viableCandidates[0].score, viableCandidates[1].score, sourceURL)
	}
	return viableCandidates[0].selection, nil
}
