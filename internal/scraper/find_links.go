package scraper

import (
	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

func FindLinks(url string) ([]Link, error) {
	collector := colly.NewCollector()
	var err error
	links := []Link{}

	collector.OnHTML("#ebooks, #public-domain, #pdf-search", func(category *colly.HTMLElement) {
		// Get the main link text and URL
		category.DOM.NextFiltered("ul").Find("li.starred strong a").Each(func(_ int, e *goquery.Selection) {
			linkName := e.Text()
			linkURL, exists := e.Attr("href")
			if exists {
				links = append(links, Link{linkName, linkURL, "Books"})
			}
		})
	})

	collector.OnError(func(r *colly.Response, e error) {
		err = e
	})

	collector.Visit(url)

	return links, err
}
