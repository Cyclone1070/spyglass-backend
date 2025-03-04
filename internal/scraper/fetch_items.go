package scraper

import (
	"github.com/PuerkitoBio/goquery"
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
		// find leaf nodes and append text to OtherText
		currentCardContent.OtherText = []string{}
		e.DOM.Find("*:not(:has(*))").Each(func(_ int, leafNode *goquery.Selection) {
			// check for redundant text
			if leafNode.Get(0).Data != "a" && leafNode.Text() != "" {
				currentCardContent.OtherText = append(currentCardContent.OtherText, leafNode.Text())
			}
		})

		cardContents = append(cardContents, currentCardContent)
	})

	collector.OnError(func(r *colly.Response, e error) {
		err = e
	})

	collector.Visit(url)
	return cardContents, err
}
