package main

import (
	"encoding/json"
	"fmt"
	"os"
	"sync"

	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper"
)

func main() {
	// high level settings
	maxConcurrency := 50
	// other setup
	fmhyLinks := []string{
		"https://fmhy.net/readingpiracyguide",
		"https://fmhy.net/videopiracyguide",
		"https://fmhy.net/gamingpiracyguide",
		"https://fmhy.net/downloadpiracyguide",
		"https://fmhy.net/linuxguide",
		"https://fmhy.net/android-iosguide",
	}
	websiteLinks := []linkscraper.WebsiteLink{}

	var wg sync.WaitGroup
	var mu sync.Mutex
	pool := make(chan struct{}, maxConcurrency)

	for _, link := range fmhyLinks {
		wg.Add(1)
		pool <- struct{}{}
		go func(link string) {
			defer func() { <-pool }()
			defer wg.Done()
			links, err := linkscraper.FindWebsiteLinks(link)
			if err != nil {
				return
			}
			mu.Lock()
			websiteLinks = append(websiteLinks, links...)
			mu.Unlock()
		}(link)
	}
	wg.Wait()

	searchURL := []linkscraper.SearchLink{}
	validCounts := make(map[string]int)
	invalidCounts := make(map[string]int)
	processedCount := 0
	totalLinks := len(websiteLinks)
	println("Processing links...")

	for _, link := range websiteLinks {
		wg.Add(1)
		pool <- struct{}{}

		go func(link linkscraper.WebsiteLink) {
			defer func() { <-pool }()
			defer wg.Done()

			searchLink, err := linkscraper.FindSearchLink(link)
			mu.Lock()
			defer mu.Unlock()
			processedCount++
			if err != nil {
				invalidCounts[link.Category]++
			} else {
				validCounts[link.Category]++
				searchURL = append(searchURL, searchLink)
			}
			fmt.Printf("\rProcessed %d/%d links", processedCount, totalLinks)
		}(link)
	}
	wg.Wait()
	fmt.Println()

	file, err := os.Create("searchLinks.json")
	if err != nil {
		println("Error creating JSON file:", err.Error())
		return
	}
	defer file.Close()

	encoder := json.NewEncoder(file)
	encoder.SetIndent("", "    ")
	if err := encoder.Encode(searchURL); err != nil {
		println("Error encoding JSON:", err.Error())
		return
	}

	println("Scraping completed. Results saved to searchLinks.json")
	println("\nLink Status by Category:")
	allCategories := make(map[string]struct{})
	for category := range validCounts {
		allCategories[category] = struct{}{}
	}
	for category := range invalidCounts {
		allCategories[category] = struct{}{}
	}

	for category := range allCategories {
		valid := validCounts[category]
		invalid := invalidCounts[category]
		fmt.Printf("Category: %s\n", category)
		fmt.Printf("  Valid Links: %d\n", valid)
		fmt.Printf("  Invalid Links: %d\n", invalid)
	}
}
