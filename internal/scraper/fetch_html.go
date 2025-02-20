package scraper

import (
	"fmt"

	"github.com/gocolly/colly/v2"
)

func FetchHTML(url string) string {
	var responseBody string
	var err string
	collector := colly.NewCollector()

	collector.OnError(func(r *colly.Response, e error) {
		err = fmt.Sprintf("%d: %s", r.StatusCode, e.Error())
	})

	collector.OnHTML("html", func(e *colly.HTMLElement) {
		responseBody, _ = e.DOM.Html()
	})

	collector.Visit(url)
	if err == "" {
		return responseBody
	} else {
		return err
	}
}
