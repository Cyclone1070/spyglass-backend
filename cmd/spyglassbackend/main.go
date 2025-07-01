package main

import (
	"sync"

	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper"
)

func main() {
	fmhyLinks := []string{
		// "https://fmhy.net/readingpiracyguide",
		// "https://fmhy.net/videopiracyguide",
		// "https://fmhy.net/gamingpiracyguide",
		"https://fmhy.net/downloadpiracyguide",
		// "https://fmhy.net/linuxguide",
		// "https://fmhy.net/android-iosguide",
	}
	websiteLinks := []linkscraper.WebsiteLink{}
	for _, link := range fmhyLinks {
		print("Scraping links from " + link + "...\n")
		links, err := linkscraper.FindWebsiteLinks(link)
		if err != nil {
			println("Error scraping " + link + ": ", err.Error())
			continue
		}
		println("Found", len(links), "links in", link)
		websiteLinks = append(websiteLinks, links...)
	}
	var wg sync.WaitGroup
	var mu sync.Mutex
	for _, link := range websiteLinks {
		wg.Add(1)
		go func(link linkscraper.WebsiteLink) {
			defer wg.Done()
			searchLink, err := linkscraper.FindSearchLinks(link)
			if err != nil {
				mu.Lock()
				println("Error finding search link for", link.Title, ":", err.Error())
				mu.Unlock()
				return
			}
			mu.Lock()
			println("Title:", link.Title)
			println("URL:", link.URL)
			println("Category:", link.Category)
			println("Search URL:", searchLink.URL)
			println("Search Query Param:", searchLink.QueryParamName)
			println()
			mu.Unlock()
		}(link)
	}
	wg.Wait()
}
