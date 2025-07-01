package linkscraper

import (
	"errors"
	"strings"

	"github.com/gocolly/colly/v2"
)

// FindSearchLinks extracts search URLs from a given link.
func FindSearchLinks(link WebsiteLink) (SearchLink, error) {
	// Check if the link has a search form.
	collector := colly.NewCollector()
	searchLinks := []SearchLink{}
	var err error

	collector.OnHTML("form[action]:has(input[type=search], input[type=text])", func(e *colly.HTMLElement) {
		method := e.Attr("method")
		if strings.ToLower(method) == "post" || strings.ToLower(method) == "dialog" {
			return
		}
		action := e.Attr("action")
		searchURL := e.Request.AbsoluteURL(action)
		inputElement := e.DOM.Find("input[type=search], input[type=text]")
		if inputElement.Length() != 1 {
			err = errors.New("multiple potential search params found in link: " + link.URL)
			return
		}
		queryParam, exists := inputElement.Attr("name")
		if exists {
			searchLinks = append(searchLinks, SearchLink{link.Title, searchURL, link.Category, queryParam})
		}
	})
	
	collector.OnError(func(r *colly.Response, collectorError error) {
		err = errors.New("error visiting link: " + link.URL + " - " + collectorError.Error())
	})

	collector.Visit(link.URL)
	if err != nil {
		return SearchLink{}, err
	} else if len(searchLinks) == 1 {
		return searchLinks[0], err
	} else if len(searchLinks) > 1 {
		return SearchLink{}, errors.New("multiple search URLs found in link: " + link.URL)
	} else {
		return SearchLink{}, errors.New("no search URL found in link: " + link.URL)
	}
}
