// Package cardscraper provides an advanced, two-tiered scraping engine to automatically
// discover the CSS selector for repeating result "cards" on a website's search page.
// It is designed to be browser-free, using only the gocolly library for HTTP requests.
//
// The core strategy is a fallback system:
// Tier 1 attempts a fast and precise "differential scrape" by comparing a page with
// no results to a page with results. This is ideal for classic, server-rendered websites.
//
// Tier 2, the fallback, performs a "frequency analysis" on a single results page to
// find the most commonly repeated element pattern. This is more robust for sites where
// the differential method fails, such as some Single Page Applications (SPAs).
package cardscraper

import (
	"errors"
	"fmt"
	"net/url"
	"sort"
	"strings"

	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper" // Assumes this path is correct
	"github.com/Cyclone1070/spyglass-backend/internal/utils"       // Using the centralized constructor
	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

// ResultCardSelector holds the final discovered selector for a given site,
// combining the original link information with the new selector.
type ResultCardSelector struct {
	Title    string `json:"title"`
	URL      string `json:"url"`
	Selector string `json:"selector"`
}

// Global query variables used for the scraping strategies.
var (
	// commonQuery is a generic word guaranteed to return results on most English-language sites.
	commonQuery = "the"
	// dumbQuery is a nonsensical string guaranteed to return no results.
	dumbQuery = "asdfghjklqwerty12345"
	// specialisedQueries provides category-specific common words to use as a fallback
	// if the generic query fails (e.g., due to a timeout on a very large site).
	specialisedQueries = map[string]string{
		"Books":  "murder",
		"Movies": "love",
	}
)

// FindResultCardSelector is the main entry point for the card scraping engine.
// It orchestrates the two-tiered scraping strategy, trying the fast differential
// scrape first and falling back to the more robust frequency analysis if needed.
// It takes a SearchLink, which contains the URL template for the site's search page.
func FindResultCardSelector(searchLink linkscraper.SearchLink) (ResultCardSelector, error) {
	// --- Tier 1: Attempt the fast, differential scrape (The Scalpel) ---
	selector, err := runDifferentialScrape(searchLink)
	if err == nil {
		return ResultCardSelector{
			Title:    searchLink.Title,
			URL:      searchLink.SearchURL,
			Selector: selector,
		}, nil
	}

	// --- Tier 2: Fallback to single-page frequency analysis (The Wrench) ---
	selector, err = runFrequencyAnalysisScrape(searchLink)
	if err == nil {
		return ResultCardSelector{
			Title:    searchLink.Title,
			URL:      searchLink.SearchURL,
			Selector: selector,
		}, nil
	}

	return ResultCardSelector{}, fmt.Errorf("all gocolly scraping tiers failed for %s: %w", searchLink.URL, err)
}

// runDifferentialScrape performs the Tier 1 strategy. It scrapes a "no results" page
// and a "with results" page to create a blacklist of common elements, then "diffs"
// them to find the unique container holding the result cards. This method is highly
// accurate for classic server-side rendered (SSR) websites.
func runDifferentialScrape(searchLink linkscraper.SearchLink) (string, error) {
	// diffAnalysis is a closure that contains the core logic for analyzing the difference
	// between two pages. This allows us to reuse it for the fallback query.
	diffAnalysis := func(withResultsDoc *goquery.Document, noResultsBlacklist map[string]struct{}) (string, error) {
		var candidates []struct {
			selection *goquery.Selection
			depth     int
		}
		withResultsDoc.Find("body *").Each(func(i int, el *goquery.Selection) {
			if el.Children().Length() < 2 {
				return
			}
			sig := getElementSignature(el)
			if _, exists := noResultsBlacklist[sig]; !exists {
				if cardSig := findRepeatingChildSignature(el); cardSig != "" {
					candidates = append(candidates, struct {
						selection *goquery.Selection
						depth     int
					}{el, el.Parents().Length()})
				}
			}
		})

		if len(candidates) == 0 {
			return "", errors.New("diff failed: could not find any unique container with repeating children")
		}
		sort.Slice(candidates, func(i, j int) bool { return candidates[i].depth < candidates[j].depth })
		bestContainer := candidates[0].selection

		containerSignature := getElementSignature(bestContainer)
		cardSignature := findRepeatingChildSignature(bestContainer)
		if cardSignature == "" {
			return "", fmt.Errorf("found container (%s), but failed to find repeating cards inside", containerSignature)
		}
		return containerSignature + " > " + cardSignature, nil
	}

	// --- Execution Flow with Fallback ---
	noResultsURL := fmt.Sprintf(searchLink.SearchURL, url.QueryEscape(dumbQuery))
	noResultsBlacklist, err := getElementSignatureSet(noResultsURL)
	if err != nil {
		return "", fmt.Errorf("failed to scrape 'no results' page: %w", err)
	}

	// First attempt with the common query.
	withResultsURL := fmt.Sprintf(searchLink.SearchURL, url.QueryEscape(commonQuery))
	withResultsDoc, err := getGoqueryDoc(withResultsURL)
	if err == nil {
		selector, analysisErr := diffAnalysis(withResultsDoc, noResultsBlacklist)
		if analysisErr == nil {
			return selector, nil // Primary strategy succeeded.
		}
	}

	// If the first attempt failed, try the specialised query.
	if specialisedQuery, ok := specialisedQueries[searchLink.Category]; ok {
		withResultsURL = fmt.Sprintf(searchLink.SearchURL, url.QueryEscape(specialisedQuery))
		withResultsDoc, err = getGoqueryDoc(withResultsURL)
		if err != nil {
			return "", fmt.Errorf("failed to scrape 'with results' page on both common and specialised queries: %w", err)
		}
		return diffAnalysis(withResultsDoc, noResultsBlacklist)
	}

	return "", errors.New("primary diff failed and no specialised query was available")
}

// runFrequencyAnalysisScrape performs the Tier 2 strategy. It scrapes a single
// results page and performs a deep analysis to find the most frequently repeated
// element pattern. This is effective for SPAs or when differential scraping fails.
func runFrequencyAnalysisScrape(searchLink linkscraper.SearchLink) (string, error) {
	// First attempt with the common query.
	withResultsURL := fmt.Sprintf(searchLink.SearchURL, url.QueryEscape(commonQuery))
	doc, err := getGoqueryDoc(withResultsURL)
	if err == nil {
		selector, analysisErr := findBestRepeatingSignature(doc.Selection)
		if analysisErr == nil {
			return selector, nil // Primary strategy succeeded.
		}
	}

	// If the first attempt failed, try the specialised query.
	if specialisedQuery, ok := specialisedQueries[searchLink.Category]; ok {
		withResultsURL = fmt.Sprintf(searchLink.SearchURL, url.QueryEscape(specialisedQuery))
		doc, err = getGoqueryDoc(withResultsURL)
		if err != nil {
			return "", fmt.Errorf("failed to scrape on both common and specialised queries: %w", err)
		}
		return findBestRepeatingSignature(doc.Selection)
	}

	return "", errors.New("primary frequency analysis failed and no specialised query available")
}

// findBestRepeatingSignature performs a deep "Child Frequency Analysis" across an
// entire document. It scores potential card signatures based on their repetition
// count under a common parent and their depth in the DOM.
func findBestRepeatingSignature(doc *goquery.Selection) (string, error) {
	candidateScores := make(map[string]int)
	doc.Find("body *").Each(func(i int, parent *goquery.Selection) {
		childSignatures := make(map[string]int)
		parent.Children().Each(func(_ int, child *goquery.Selection) {
			sig := getElementSignature(child)
			if sig != "" {
				childSignatures[sig]++
			}
		})
		for sig, count := range childSignatures {
			if count > 1 {
				candidateScores[sig] += (count * 5) + parent.Parents().Length()
			}
		}
	})
	if len(candidateScores) == 0 {
		return "", errors.New("frequency analysis failed: no elements with repeating signatures found")
	}
	var bestSig string
	maxScore := 0
	for sig, score := range candidateScores {
		if score > maxScore {
			maxScore = score
			bestSig = sig
		}
	}
	tieCount := 0
	for _, score := range candidateScores {
		if score == maxScore {
			tieCount++
		}
	}
	if tieCount > 1 {
		return "", fmt.Errorf("frequency analysis failed: found %d candidates with same top score", tieCount)
	}
	return bestSig, nil
}

// findRepeatingChildSignature finds the most common direct child signature within a
// given container element. This is resilient to interstitial elements like ads.
func findRepeatingChildSignature(container *goquery.Selection) string {
	childSignatures := make(map[string]int)
	container.Children().Each(func(i int, child *goquery.Selection) {
		sig := getElementSignature(child)
		if sig != "" {
			childSignatures[sig]++
		}
	})
	var bestSig string
	maxScore := 1 // Must appear more than once to be "repeating".
	for sig, score := range childSignatures {
		if score > maxScore {
			maxScore = score
			bestSig = sig
		}
	}
	return bestSig
}

// getElementSignature creates a precise and stable CSS selector for a single element.
// It prioritizes IDs, then uses the tag name and a sorted list of all classes,
// ensuring a consistent signature.
func getElementSignature(element *goquery.Selection) string {
	if element.Length() == 0 {
		return ""
	}
	tag := goquery.NodeName(element)
	if tag == "" {
		return ""
	}
	if id, ok := element.Attr("id"); ok && id != "" {
		return tag + "#" + id
	}
	var builder strings.Builder
	builder.WriteString(tag)
	if class, ok := element.Attr("class"); ok && class != "" {
		classes := strings.Fields(class)
		if len(classes) > 0 {
			sort.Strings(classes)
			builder.WriteString(".")
			builder.WriteString(strings.Join(classes, "."))
		}
	}
	return builder.String()
}

// getGoqueryDoc is a low-level helper that fetches a URL and returns its parsed
// goquery document. It creates a new, configured, and thread-safe collector
// for each request by calling the centralized utils.ConfiguredCollector function.
func getGoqueryDoc(url string) (*goquery.Document, error) {
	c := utils.ConfiguredCollector() // Correctly using your utils package
	var doc *goquery.Document
	var finalErr error
	var onHTMLFired bool
	c.OnHTML("html", func(e *colly.HTMLElement) {
		onHTMLFired = true
		doc, finalErr = goquery.NewDocumentFromReader(strings.NewReader(string(e.Response.Body)))
	})
	c.OnError(func(r *colly.Response, e error) {
		if finalErr == nil {
			finalErr = fmt.Errorf("request to %s failed with status %d: %w", r.Request.URL, r.StatusCode, e)
		}
	})
	c.Visit(url)
	if finalErr != nil {
		return nil, finalErr
	}
	if !onHTMLFired {
		return nil, errors.New("request succeeded, but response was not HTML")
	}
	if doc == nil {
		return nil, errors.New("HTML callback ran but failed to produce a document")
	}
	return doc, nil
}

// getElementSignatureSet is a helper that scrapes a URL and returns a map (acting as a set)
// of all unique element signatures found on the page. This is used to create the
// blacklist for the differential scraping strategy.
func getElementSignatureSet(url string) (map[string]struct{}, error) {
	doc, err := getGoqueryDoc(url)
	if err != nil {
		return nil, err
	}
	signatureSet := make(map[string]struct{})
	doc.Find("body *").Each(func(i int, el *goquery.Selection) {
		signature := getElementSignature(el)
		if signature != "" {
			signatureSet[signature] = struct{}{}
		}
	})
	return signatureSet, nil
}
