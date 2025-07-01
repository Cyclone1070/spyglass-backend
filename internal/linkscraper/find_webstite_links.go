// Package linkscraper provides functionality to scrape links from fmhy urls
package linkscraper

import (
	"errors"
	"strconv"
	"strings"

	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

var SkipKeywords = []string{"wiki", "github", "FOSS", "guide", "CSE", "reddit", "t.me", "mozilla"}

// FindWebsiteLinks returns a list of links from the given URL, categorised by type,
// excluding links that contain certain keywords or are not relevant.
func FindWebsiteLinks(url string) ([]WebsiteLink, error) {
	collector := colly.NewCollector()
	var err error
	links := []WebsiteLink{}
	categories := []struct {
		name     string
		selector string
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
	for _, category := range categories {
		collector.OnHTML(category.selector, func(categoryHTML *colly.HTMLElement) {
			scrapeLinkFromCategory(categoryHTML, category.name, &links)
		})
	}

	collector.OnError(func(r *colly.Response, e error) {
		err = errors.New("error visiting link: " + url + " - " + e.Error())
	})

	collector.Visit(url)

	return links, err
}

// helper function to scrape links from a category html element
func scrapeLinkFromCategory(category *colly.HTMLElement, categoryName string, links *[]WebsiteLink) {
	category.DOM.NextAllFiltered("ul").First().Find("li a").Each(func(_ int, e *goquery.Selection) {
		// skip if the link text is an integer (mirror links)
		if isInteger(e.Text()) {
			return
		}
		// skip if globe icon is present
		if e.Closest("li").Find(".i-twemoji-globe-with-meridians").Length() > 0 {
			return
		}
		linkName := e.Text()
		linkURL, exists := e.Attr("href")
		if exists {
			for _, keyword := range SkipKeywords {
				if strings.Contains(linkURL, keyword) {
					return // skip links containing skip keywords
				}
				if strings.Contains(e.Closest("li").Text(), keyword) {
					return
				}
			}
			*links = append(*links, WebsiteLink{linkName, linkURL, categoryName})
		}
	})
}

func isInteger(s string) bool {
	_, err := strconv.Atoi(s)
	return err == nil
}
