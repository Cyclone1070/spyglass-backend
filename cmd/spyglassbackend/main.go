package main

import (
	"sync"

	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper"
)

func main() {
	websiteLinks, err := linkscraper.FindWebsiteLinks("https://fmhy.net/readingpiracyguide")
	if err != nil {
		println("Error:", err.Error())
		return
	}
	var wg sync.WaitGroup
	for _, link := range websiteLinks {
		wg.Add(1)
		go func(link linkscraper.WebsiteLink) {
			defer wg.Done()
			searchLink, err := linkscraper.FindSearchLinks(link)
			if err != nil {
				return
			}
			println("Title:", link.Title)
			println("URL:", link.URL)
			println("Category:", link.Category)
			println("Search URL:", searchLink.URL)
			println("Search Query Param:", searchLink.QueryParamName)
			println()
		}(link)
	}
	wg.Wait()
}
