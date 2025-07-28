// Package linkscraper provides functionality to scrape links from fmhy urls
package linkscraper

import (
	"context"
	"log"
	"strconv"
	"strings"
	"time"

	"github.com/PuerkitoBio/goquery"
	"github.com/chromedp/chromedp"
)

var SkipKeywords = []string{"wiki", "github", "FOSS", "guide", "CSE", "reddit", "t.me", "mozilla", "greasyfork", "discord", "telegram"}

var categories = []struct {
	name     string
	selector string // CSS selector for the category header(s)
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

// FindWebsiteLinks returns a list of links from the given URL, categorised by type,
// excluding links that contain certain keywords or are not relevant.
func FindWebsiteLinks(url string, allocatorCtx context.Context) ([]WebsiteLink, error) {
	// --- STAGE 1: Use chromedp to get the fully-rendered HTML ---

	// Create a new tab context from the main browser allocator
	newTabCtx, cancel := chromedp.NewContext(allocatorCtx)
	defer cancel()

	// Add a timeout to this specific tab's operations
	taskCtx, cancel := context.WithTimeout(newTabCtx, 30*time.Second)
	defer cancel()

	var htmlContent string
	err := chromedp.Run(taskCtx,
		chromedp.Navigate(url),
		chromedp.WaitVisible("html", chromedp.ByQuery), // Wait for the body to be visible
		chromedp.OuterHTML("html", &htmlContent),
	)
	if err != nil {
		return nil, err // Return error if chromedp fails
	}
	// --- STAGE 2: Parse the HTML with goquery and extract links ---

	doc, err := goquery.NewDocumentFromReader(strings.NewReader(htmlContent))
	if err != nil {
		return nil, err // Return error if HTML parsing fails
	}

	allLinks := []WebsiteLink{}

	// Iterate over each defined category
	for _, category := range categories {
		// Find all header elements matching the category's selector
		doc.Find(category.selector).Each(func(i int, headerSelection *goquery.Selection) {
			// For each header found, scrape the links that follow it
			foundLinks := scrapeCategoryLinks(headerSelection, category.name)
			if len(foundLinks) > 0 {
				allLinks = append(allLinks, foundLinks...)
			}
		})
	}

	log.Printf("Scraping %s complete. Found %d total links.", url, len(allLinks))
	return allLinks, nil
}

// scrapeCategoryLinks is the pure goquery version of the original helper function.
// It finds links within the first <ul> following a given header element.
func scrapeCategoryLinks(headerSelection *goquery.Selection, categoryName string) []WebsiteLink {
	var links []WebsiteLink

	// Find the first <ul> that is the next sibling of the header.
	listSelection := headerSelection.NextAllFiltered("ul").First()

	// Find all links within that list and iterate through them.
	listSelection.Find("li a").Each(func(_ int, linkSelection *goquery.Selection) {
		linkText := linkSelection.Text()

		// Rule 1: Skip if the link text is just a number (e.g., mirror links).
		if isInteger(linkText) {
			return // Skips to the next link in the .Each loop
		}

		// Rule 2: Skip if a globe icon is present in the parent list item.
		parentLi := linkSelection.Closest("li")
		if parentLi.Find(".i-twemoji-globe-with-meridians").Length() > 0 {
			return
		}
		starred := parentLi.HasClass("starred")

		// Rule 3: Check against skip keywords.
		if linkURL, exists := linkSelection.Attr("href"); exists {
			parentLiText := parentLi.Text() // Get parent text once to be efficient.

			for _, keyword := range SkipKeywords {
				if strings.Contains(linkURL, keyword) || strings.Contains(parentLiText, keyword) {
					return // Skip if URL or surrounding text contains a keyword.
				}
			}

			// If all checks pass, add the link to our results.
			links = append(links, WebsiteLink{Title: linkText, URL: linkURL, Category: categoryName, Starred: starred})
		}
	})

	return links
}

// isInteger checks if a string can be converted to an integer.
func isInteger(s string) bool {
	_, err := strconv.Atoi(s)
	return err == nil
}
