// Package linkscraper provides functionality to scrape links from fmhy urls
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

type WebsiteLink struct {
	Title    string
	URL      string
	Category string
	Starred  bool
}

var SkipKeywords = []string{"wiki", "github", "FOSS", "guide", "CSE", "reddit", "t.me", "mozilla", "greasyfork", "discord", "telegram", "vinegar", "guide", "launcher", "cli", "tui", "manager", "wine", "frontend"}

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

// FindWebsiteLinks returns a list of links from the given URL using the gocolly framework.
// This version makes direct HTTP requests and does not use a browser.
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
		// e.DOM is a *goquery.Document, which is exactly what our old code used!
		// This allows us to reuse all the previous parsing logic.

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

// scrapeCategoryLinks requires NO CHANGES. It is pure goquery logic that works
// perfectly with the *goquery.Selection provided by gocolly's HTMLElement.
func scrapeCategoryLinks(headerSelection *goquery.Selection, categoryName string) []WebsiteLink {
	var links []WebsiteLink

	listSelection := headerSelection.NextAllFiltered("ul").First()
	listSelection.Find("li a").Each(func(_ int, linkSelection *goquery.Selection) {
		linkText := linkSelection.Text()
		if isInteger(linkText) {
			return
		}
		parentLi := linkSelection.Closest("li")
		if parentLi.Find(".i-twemoji-globe-with-meridians").Length() > 0 {
			return
		}
		starred := parentLi.HasClass("starred")
		if linkURL, exists := linkSelection.Attr("href"); exists {
			parentLiText := parentLi.Text()
			for _, keyword := range SkipKeywords {
				if strings.Contains(linkURL, keyword) || strings.Contains(parentLiText, keyword) {
					return
				}
			}
			links = append(links, WebsiteLink{Title: linkText, URL: linkURL, Category: categoryName, Starred: starred})
		}
	})
	return links
}

// isInteger requires NO CHANGES. It's pure Go standard library logic.
func isInteger(s string) bool {
	_, err := strconv.Atoi(s)
	return err == nil
}
