package scraper

import (
	"fmt"

	"github.com/gocolly/colly/v2"
)

func FetchItems(url string) ([]Link, error) {
	var links = []Link{}
	var err error
	collector := colly.NewCollector()

	collector.OnError(func(r *colly.Response, e error) {
		err = fmt.Errorf("%d: %s", r.StatusCode, e.Error())
	})

	collector.OnHTML("a[href]", func(e *colly.HTMLElement) {
		links = append(links, Link{e.Text, e.Attr("href")})
	})

	collector.Visit(url)
	return links, err
}
