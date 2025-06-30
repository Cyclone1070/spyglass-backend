package linkscraper

import (
	"errors"

	"github.com/gocolly/colly/v2"
)

// FindSearchLinks extracts search URLs from a given link.
func FindSearchLinks(link WebsiteLink) (SearchLink, error) {
	// Check if the link has a search form.
	collector := colly.NewCollector()
	searchLinks := []SearchLink{}

	collector.OnHTML("form:has(input[type=search], input[type=text])", func(e *colly.HTMLElement) {
		action := e.Attr("action")
		searchURL := e.Request.AbsoluteURL(action)
		inputElement := e.DOM.Find("input[type=search], input[type=text]")
		queryParam, exists := inputElement.Attr("name")
		if exists {
			searchLinks = append(searchLinks, SearchLink{link.Title, searchURL, link.Category, queryParam})
		}
	})

	collector.Visit(link.URL)
	if len(searchLinks) == 1 {
		return searchLinks[0], nil
	} else if len(searchLinks) > 1 {
		return SearchLink{}, errors.New("multiple search URLs found")
	} else {
		return SearchLink{}, errors.New("no search URL found")
	}
}
