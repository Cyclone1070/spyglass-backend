package linkscraper

import (
	"context"
	"errors"
	"fmt"
	"regexp"
	"sort"
	"strings"
	"time"

	"github.com/PuerkitoBio/goquery"
	"github.com/chromedp/chromedp"
)

// FindSearchInput orchestrates the discovery of the primary search input on a page.
// It uses helper functions to first filter out irrelevant forms and then to score
// the remaining candidates to find the single best one.
func FindSearchInput(link WebsiteLink, allocatorCtx context.Context) (SearchInput, error) {
	newTab, cancel := chromedp.NewContext(allocatorCtx)
	defer cancel()

	tab, cancel := context.WithTimeout(newTab, 30*time.Second)
	defer cancel()

	var htmlContent string
	err := chromedp.Run(tab,
		chromedp.Navigate(link.URL),
		chromedp.WaitVisible("html", chromedp.ByQuery),
		chromedp.OuterHTML("html", &htmlContent),
	)
	if err != nil {
		return SearchInput{}, fmt.Errorf("failed to navigate to %s: %w", link.URL, err)
	}

	doc, err := goquery.NewDocumentFromReader(strings.NewReader(htmlContent))
	if err != nil {
		return SearchInput{}, fmt.Errorf("failed to parse HTML from %s: %w", link.URL, err)
	}

	// Phase 1: Use the 'isLikelySearchForm' gatekeeper to filter out forms
	// that are obviously for login, comments, subscriptions, etc.
	searchForms := doc.Find("form").FilterFunction(isLikelySearchForm)

	if searchForms.Length() == 0 {
		return SearchInput{}, errors.New("no likely search forms found on page: " + link.URL)
	}

	// Phase 2: From the filtered list, find forms that contain exactly one search input.
	var validSingleInputs []*goquery.Selection
	searchForms.Each(func(i int, formSelection *goquery.Selection) {
		inputsInThisForm := formSelection.Find("input[type='search'], input[type='text']")
		if inputsInThisForm.Length() == 1 {
			validSingleInputs = append(validSingleInputs, inputsInThisForm)
		}
	})

	// Phase 3: Pass the high-quality candidates to the scoring engine to choose the best one.
	selection, err := chooseBestSearchInput(validSingleInputs, link.URL)
	if err != nil {
		return SearchInput{}, err
	}

	// We now have a confident winner. Proceed to extract final details.
	form := selection.Closest("form")
	method, exists := form.Attr("method")
	if !exists || method == "" {
		method = "get"
	}
	method = strings.ToLower(method)
	if method != "get" && method != "post" {
		method = "get"
	}

	cssSelector := getSelectorPath(selection)
	return SearchInput{WebsiteLink: link, InputSelector: cssSelector, Method: method}, nil
}

// Pre-compile the regex for efficiency. This will be used to find non-search keywords.
var nonSearchKeywords = regexp.MustCompile(
	`login|log in|sign ?in|username|password|register|sign ?up|subscribe|newsletter|contact|comment|forgot|e-mail|email`,
)

// isLikelySearchForm is a gatekeeper filter. It returns 'false' if a form is
// clearly for a purpose other than search.
func isLikelySearchForm(i int, s *goquery.Selection) bool {
	// Disqualification 1: Presence of a password field is a definitive "not a search form".
	if s.Find(`input[type="password"]`).Length() > 0 {
		return false
	}

	// Disqualification 2: Presence of a textarea is a strong signal for a comment/contact form.
	if s.Find("textarea").Length() > 0 {
		return false
	}

	// Disqualification 3: Check for keywords in high-value text elements like headings and buttons.
	var formTextBuilder strings.Builder
	s.Find("h1, h2, h3, button, a[role='button'], input[type='submit']").Each(func(_ int, el *goquery.Selection) {
		formTextBuilder.WriteString(el.Text())
		formTextBuilder.WriteString(" ")
	})

	// Check the combined text against our keyword blacklist.
	if nonSearchKeywords.MatchString(strings.ToLower(formTextBuilder.String())) {
		return false
	}

	// If no disqualifications were met, it's a likely candidate.
	return true
}

// chooseBestSearchInput is a scoring engine that takes a pre-vetted list of
// candidates and uses heuristics to determine the single best one.
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

	// --- Scoring Engine ---
	for i := range scoredCandidates {
		sel := scoredCandidates[i].selection
		form := sel.Closest("form")
		var reasons []string
		var positiveSignals int

		// Phase 1: Base Score (assumes the form is already pre-vetted).
		// This heavily favors the modern HTML5 standard.
		if inputType, _ := sel.Attr("type"); inputType == "search" {
			scoredCandidates[i].score += 100
			reasons = append(reasons, "+100 (Base:type='search')")
		} else {
			scoredCandidates[i].score += 10
			reasons = append(reasons, "+10 (Base:type='text')")
		}

		// Phase 2: Contextual & Attribute Scoring
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

		// Phase 3: Penalties for Negative Context
		if sel.Closest("footer").Length() > 0 {
			scoredCandidates[i].score -= 200
			reasons = append(reasons, "-200 (in <footer>)")
		}
		if sel.Closest("aside, .sidebar").Length() > 0 {
			scoredCandidates[i].score -= 100
			reasons = append(reasons, "-100 (in sidebar)")
		}

		// Phase 4: Certainty Bonus
		if positiveSignals >= 3 {
			scoredCandidates[i].score += 50
			reasons = append(reasons, "+50 (Certainty Bonus)")
		}
		scoredCandidates[i].reasoning = strings.Join(reasons, ", ")
	}

	// --- Final Selection Process ---
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

// getSelectorPath creates a reasonably unique CSS selector for a given element.
// This function does not need to be changed.
func getSelectorPath(selection *goquery.Selection) string {
	var pathParts []string

	for selection.Length() > 0 && goquery.NodeName(selection) != "html" {
		var builder strings.Builder
		nodeName := goquery.NodeName(selection)
		builder.WriteString(nodeName)

		// 1. Add the ID if it exists. (e.g., "div#main-content")
		if id, ok := selection.Attr("id"); ok && id != "" {
			builder.WriteString("#")
			builder.WriteString(id)
			// If we have an ID, this path part is unique, no need for classes/attrs.
			pathParts = append([]string{builder.String()}, pathParts...)
			selection = selection.Parent()
			continue
		}

		// 2. Add specific, useful attributes.
		if action, ok := selection.Attr("action"); ok {
			builder.WriteString(fmt.Sprintf(`[action="%s"]`, action))
		} else if name, ok := selection.Attr("name"); ok {
			builder.WriteString(fmt.Sprintf(`[name="%s"]`, name))
		}

		// 3. EFFICIENTLY add all class names.
		if classStr, ok := selection.Attr("class"); ok && classStr != "" {
			start := 0
			for start < len(classStr) {
				for start < len(classStr) && classStr[start] == ' ' {
					start++
				}
				if start >= len(classStr) {
					break
				}
				end := strings.IndexByte(classStr[start:], ' ')
				if end == -1 {
					end = len(classStr)
				} else {
					end += start
				}
				builder.WriteString(".")
				builder.WriteString(classStr[start:end])
				start = end
			}
		}

		pathParts = append([]string{builder.String()}, pathParts...)
		selection = selection.Parent()
	}

	return "html > " + strings.Join(pathParts, " > ")
}
