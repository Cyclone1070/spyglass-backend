package scraper

import (
	"github.com/gocolly/colly/v2"
)

func FetchItems(url string, cardPath string, query string) ([]CardContent, error) {
	var cardContents = []CardContent{}
	var err error

	collector := colly.NewCollector()

	collector.OnHTML(cardPath, func(e *colly.HTMLElement) {
		var currentCardContent CardContent
		currentCardContent.Title = e.ChildText("a")
		currentCardContent.Url = e.ChildAttr("a", "href")
		currentCardContent.OtherText = []string{}
		cardContents = append(cardContents, currentCardContent)
	})

	collector.OnError(func(r *colly.Response, e error) {
		err = e
	})

	collector.Visit(url)
	return cardContents, err
}
