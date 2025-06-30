package linkscraper

import "github.com/gocolly/colly/v2"

// FindSearchLinks extracts search URLs from a given link.
func FindSearchLinks(link WebsiteLink) (SearchLink, error) {
	// Check if the link has a search form.
	collector := colly.NewCollector()
	var err error
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
	if len(searchLinks) > 0 {
		return searchLinks[0], err
	}
	return SearchLink{}, nil
	
}
