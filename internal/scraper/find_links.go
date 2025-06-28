package scraper

import (
	"strconv"

	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

func FindLinks(url string) ([]Link, error) {
	collector := colly.NewCollector()
	var err error
	links := []Link{}
	categories := []struct{
		name string
		selector string
	} {
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
		err = e
	})

	collector.Visit(url)

	return links, err
}

// helper function to scrape links from a category html element
func scrapeLinkFromCategory(category *colly.HTMLElement, categoryName string, links *[]Link) {
	category.DOM.NextFiltered("ul").Find("li.starred a").Each(func(_ int, e *goquery.Selection) {
		// skip if the link text is an integer (mirror links)
		if isInteger(e.Text()) {
			return
		}
		linkName := e.Text()
		linkURL, exists := e.Attr("href")
		if exists {
			*links = append(*links, Link{linkName, linkURL, categoryName})
		}
	})
}
func isInteger(s string) bool {
	_, err := strconv.Atoi(s)
	return err == nil
}
