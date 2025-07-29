// Package linkscraper provides functionality to scrape and discover website links.
// It includes tools for extracting categorized links from curated lists (like those on fmhy.net)
// and for automatically finding a website's search functionality.
package linkscraper

import (
	"fmt"
	"log"
	"strconv"
	"strings"

	"github.com/Cyclone1070/spyglass-backend/internal/utils"
	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

// WebsiteLink represents a single hyperlink scraped from a webpage.
// It holds the core information about the link's text, destination URL,
// the category it belongs to, and whether it was marked as "starred".
type WebsiteLink struct {
	Title    string // The visible text of the link.
	URL      string // The absolute URL the link points to.
	Category string // The high-level category this link was found under.
	Starred  bool   // True if the link was marked as a favorite or important.
}

// SkipKeywords is a list of substrings used to filter out irrelevant links.
// If a link's URL or its surrounding text contains any of these keywords,
// it is ignored. This helps exclude links to wikis, guides, and other
// non-primary resources.
var SkipKeywords = []string{"wiki", "github", "FOSS", "guide", "CSE", "reddit", "t.me", "mozilla", "greasyfork", "discord", "telegram", "vinegar", "guide", "launcher", "cli", "tui", "manager", "wine", "frontend"}

// categories defines the structure for scraping. Each entry maps a high-level
// category name to the CSS selectors used to find the corresponding section(s)
// on the target webpage. This allows the scraper to process multiple, distinct
// lists of links in a single pass.
var categories = []struct {
	name     string // The name of the category (e.g., "Books", "Movies").
	selector string // A CSS selector for the header(s) of this category.
}{
	{"Books", "#ebooks, #public-domain, #pdf-search"},
	{"Movies", "#streaming-sites, #free-w-ads, #anime-streaming"},
	{"Games Download", "#download-games"},
	{"Games Repack", "#repack-games"},
	{"Abandonware/ROM", "#abandonware-retro, #rom-sites, #nintendo-roms, #sony-roms"},
	{"Mac Games", "#mac-gaming"},
	{"Linux Games", "#linux-gaming"},
	{"Windows Software", "#software-sites"},
	{"Mac Software", "#software-sites-1"},
	{"Android apps", "#modded-apks, #untouched-apks"},
	{"IOS apps", "#ios-ipas"},
}

// FindWebsiteLinks scrapes a given URL to extract categorized hyperlinks.
// It uses a pre-configured gocolly collector to make the HTTP request and
// then parses the HTML to find links based on the defined categories.
// The function iterates through each category, finds the corresponding header,
// and then scrapes the links from the unordered list that immediately follows it.
func FindWebsiteLinks(url string) ([]WebsiteLink, error) {
	// --- STAGE 1: Setup Gocolly Collector and Data Structures ---
	c := utils.ConfiguredCollector()

	allLinks := []WebsiteLink{}
	var scrapeErr error

	// --- STAGE 2: Define Callbacks ---

	// OnError is called if an error occurs during the request.
	c.OnError(func(r *colly.Response, err error) {
		scrapeErr = fmt.Errorf("gocolly request to %s failed with status %d: %w", r.Request.URL, r.StatusCode, err)
	})

	// OnHTML is the core of the scraper. It's called after the HTML is downloaded.
	// We select "html" to get the entire document at once.
	c.OnHTML("html", func(e *colly.HTMLElement) {
		// e.DOM is a *goquery.Document, which allows us to reuse our parsing logic.

		// Iterate over each defined category
		for _, category := range categories {
			// Find all header elements matching the category's selector
			e.DOM.Find(category.selector).Each(func(i int, headerSelection *goquery.Selection) {
				// For each header found, scrape the links that follow it
				foundLinks := scrapeCategoryLinks(headerSelection, category.name)
				if len(foundLinks) > 0 {
					allLinks = append(allLinks, foundLinks...)
				}
			})
		}
	})

	// --- STAGE 3: Start the Scraper ---
	// Visit is a blocking call. It will not return until the scraping is complete.
	c.Visit(url)

	// After Visit() is done, check if an error occurred in the OnError callback.
	if scrapeErr != nil {
		return nil, scrapeErr
	}

	log.Printf("Scraping %s complete. Found %d total links.", url, len(allLinks))
	return allLinks, nil
}

// scrapeCategoryLinks processes a specific section of a document, starting from a
// given header element, to extract all valid hyperlinks from the next `<ul>` list.
// It filters out unwanted links based on the global SkipKeywords list.
func scrapeCategoryLinks(headerSelection *goquery.Selection, categoryName string) []WebsiteLink {
	var links []WebsiteLink

	listSelection := headerSelection.NextAllFiltered("ul").First()
	listSelection.Find("li a").Each(func(_ int, linkSelection *goquery.Selection) {
		linkText := linkSelection.Text()
		if isInteger(linkText) {
			return // Skip purely numeric links, often used for ranking.
		}
		parentLi := linkSelection.Closest("li")
		// Skip links that are part of a "language" or "region" selector.
		if parentLi.Find(".i-twemoji-globe-with-meridians").Length() > 0 {
			return
		}
		starred := parentLi.HasClass("starred")
		if linkURL, exists := linkSelection.Attr("href"); exists {
			parentLiText := parentLi.Text()
			for _, keyword := range SkipKeywords {
				if strings.Contains(linkURL, keyword) || strings.Contains(parentLiText, keyword) {
					return // Skip if the URL or text contains a keyword.
				}
			}
			links = append(links, WebsiteLink{Title: linkText, URL: linkURL, Category: categoryName, Starred: starred})
		}
	})
	return links
}

// isInteger checks if a given string can be converted to an integer.
func isInteger(s string) bool {
	_, err := strconv.Atoi(s)
	return err == nil
}
