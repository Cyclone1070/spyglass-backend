package cardscraper

import (
	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

// FetchCardContent scrapes a given URL and extracts structured data from elements
// that match the provided CSS selector (`cardPath`). For each matched element, it extracts
// the title, the URL from the first anchor tag, and a collection of other text fragments.
//
// The function is designed to gather all meaningful text content from a "result card"
// by finding all leaf nodes within the card, extracting their text, and filtering out
// empty or redundant content (like the title text, which is handled separately).
// This provides a rich, unstructured collection of data associated with the primary link.
func FetchCardContent(url string, cardPath string, query string) ([]CardContent, error) {
	var cardContents = []CardContent{}
	var err error

	collector := colly.NewCollector()
	collector.UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.4 Safari/605.1.15"

	collector.OnHTML(cardPath, func(e *colly.HTMLElement) {
		var currentCardContent CardContent
		currentCardContent.Title = e.ChildText("a")
		currentCardContent.URL = e.ChildAttr("a", "href")
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
