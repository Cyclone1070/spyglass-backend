package scraper

import (
	"github.com/gocolly/colly/v2"
)

func FetchHTML(url string) string {
	var responseBody string
	collector := colly.NewCollector()
	collector.OnHTML("html", func(e *colly.HTMLElement) {
		responseBody, _ = e.DOM.Html()
	})
	collector.Visit(url)
	return responseBody
}
