package scraper

import (
	"fmt"
	"strings"

	"github.com/gocolly/colly/v2"
)

func FetchItems(url string, query string) ([]Link, error) {
	var links = []Link{}
	var err error

	normalizedQuery := strings.Split(strings.ToLower(query), " ")
	collector := colly.NewCollector()

	collector.OnError(func(r *colly.Response, e error) {
		err = fmt.Errorf("%d: %s", r.StatusCode, e.Error())
	})

	collector.OnHTML("a[href]", func(e *colly.HTMLElement) {
		if strings.TrimSpace(e.Text) == "" || strings.TrimSpace(e.Attr("href")) == "" {
			return
		}

		normalizedText := strings.ToLower(e.Text)

		for _, word := range normalizedQuery {
			if strings.Contains(normalizedText, word) {
				links = append(links, Link{e.Text, e.Attr("href")})
				break
			}
		}
	})

	collector.Visit(url)
	return links, err
}
