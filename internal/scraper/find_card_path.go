package scraper

import (
	"strings"

	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

func FindCardPath(url string, query string) string {
	collector := colly.NewCollector()
	// format as tag.class
	var cardPath string
	// flag to escape loop
	found := false
	normalizedQuery := strings.Split(strings.ToLower(query), " ")

	collector.OnHTML("a", func(e *colly.HTMLElement) {
		if found {
			return
		}

		normalizedText := strings.ToLower(e.Text)

		if containsAny(normalizedText, normalizedQuery) {
			parents := e.DOM.Parents()
			parents.Each(func(i int, parent *goquery.Selection) {
				parentSig := getElementSignature(parent)
				// if already found the card, append the rest of the parents to the identifier
				if found {
					cardPath = parentSig + " > " + cardPath
					return
				}
				// find the last parent that contains only 1 link
				if len(parent.Find("a").Nodes) > 1 {
					parentSig := getElementSignature(parent)
					// if the <a> tag has at least 1 wrapper element
					if i > 0 {
						card := parents.Eq(i - 1)
						cardPath = parentSig + " > " + getElementSignature(card)
					} else /* if the <a> tag is the card itself */ {
						cardPath = parentSig + " > " + getElementSignature(e.DOM)
					}
					// break the loop when found
					found = true
				}
			})
		}
	})

	collector.Visit(url)

	return cardPath
}

func containsAny(s string, substrings []string) bool {
	for _, sub := range substrings {
		if strings.Contains(s, sub) {
			return true
		}
	}
	return false
}
func getElementSignature(element *goquery.Selection) string {
	tag := goquery.NodeName(element)
	class, _ := element.Attr("class")
	if class != "" {
		class = strings.ReplaceAll(class, " ", ".")
		class = "." + class
	}
	return tag + class
}
