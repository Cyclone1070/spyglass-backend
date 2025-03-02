package scraper

import (
	"errors"
	"strings"

	"github.com/PuerkitoBio/goquery"
	"github.com/gocolly/colly/v2"
)

func FindCardPath(url string, query string) (string, error) {
	collector := colly.NewCollector()
	var err error
	// map of card paths to their number of ocurrences
	cardPaths := make(map[string]int)
	normalizedQuery := strings.Split(strings.ToLower(query), " ")

	collector.OnHTML("a", func(e *colly.HTMLElement) {
		// return if the text doesn't contain any of the query words
		if !containsAny(strings.ToLower(e.Text), normalizedQuery) {
			return
		}

		var currentCardPath string
		parents := e.DOM.Parents()

		for i := len(parents.Nodes) - 1; i >= -1; i-- {
			// if the card is the <a> tag itself
			if i == -1 {
				currentCardPath += getElementSignature(e.DOM)
				break
			}

			parent := parents.Eq(i)
			parentSig := getElementSignature(parent)
			// find the first parent that contains only 1 link
			if len(parent.Find("a").Nodes) == 1 {
				currentCardPath += parentSig
				break
			} else {
				currentCardPath += parentSig + " > "
			}
		}
		// increment the count of the card path
		cardPaths[currentCardPath]++
	})

	collector.OnError(func(r *colly.Response, e error) {
		err = e
	})

	collector.Visit(url)
	// return the path with the most ocurrences
	var mostCommonCardPath string
	for path, count := range cardPaths {
		if count > cardPaths[mostCommonCardPath] {
			mostCommonCardPath = path
			err = nil
		} else if count == cardPaths[mostCommonCardPath] {
			err = errors.New("multiple paths with the same occurence counts")
		}
	}
	return mostCommonCardPath, err
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
