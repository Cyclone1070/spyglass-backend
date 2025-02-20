package scraper

import (
	"fmt"

	"github.com/gocolly/colly/v2"
)

func FetchHTML(url string) (string, error) {
	var responseBody string
	var err error
	collector := colly.NewCollector()

	collector.OnError(func(r *colly.Response, e error) {
		err = fmt.Errorf("%d: %s", r.StatusCode, e.Error())
	})

	collector.OnHTML("html", func(e *colly.HTMLElement) {
		responseBody, _ = e.DOM.Html()
	})

	collector.Visit(url)
	return responseBody, err
}
