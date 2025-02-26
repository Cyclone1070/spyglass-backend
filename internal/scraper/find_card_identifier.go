package scraper

import (
	"strings"

	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

func FindCardIdentifier(url string, query string) string {
	// format as tag.class
	var cardIdentifier string
	found := false
	collector := colly.NewCollector()
	normalizedQuery := strings.Split(strings.ToLower(query), " ")

	collector.OnHTML("a", func(e *colly.HTMLElement) {
		if found {
			return
		}

		normalizedText := strings.ToLower(e.Text)

		for _, word := range normalizedQuery {
			if strings.Contains(normalizedText, word) {
				parents := e.DOM.Parents()
				parents.EachWithBreak(func(i int, parent *goquery.Selection) bool {
					// find the last parent that contains only 1 link
					if len(parent.Find("a").Nodes) > 1 {
						if i > 0 {
							prevParent := parents.Eq(i - 1)
							tag := goquery.NodeName(prevParent)
							className, _ := prevParent.Attr("class")
							cardIdentifier = tag + "." + className
							// break the loop when found
							found = true
							collector.OnHTMLDetach("a")
							return false
						}
					}
					return true
				})
			}
			if found {
				return
			}
		}
	})

	collector.Visit(url)

	return cardIdentifier
}
