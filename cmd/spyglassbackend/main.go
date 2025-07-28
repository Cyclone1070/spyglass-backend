package main

import (
	"context"
	"encoding/json"
	"fmt"
	"os"
	"sync"

	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper"
	"github.com/chromedp/chromedp"
)

type Results struct {
	SearchLinks []linkscraper.SearchInput `json:"searchLinks"`
}

func main() {
	println("Setting up browser instance for linkscraper...")
	allocatorCtx, cancel := chromedp.NewExecAllocator(context.Background(), chromedp.DefaultExecAllocatorOptions[:]...)
	defer cancel()

	fmhyLinks := []string{
		"https://fmhy.net/readingpiracyguide",
		"https://fmhy.net/videopiracyguide",
		"https://fmhy.net/gamingpiracyguide",
		"https://fmhy.net/downloadpiracyguide",
		"https://fmhy.net/linuxguide",
		"https://fmhy.net/android-iosguide",
	}
	websiteLinks := []linkscraper.WebsiteLink{}
	var websiteLinkWg sync.WaitGroup
	var websiteLinkMu sync.Mutex
	println("Scraping links from fmhy...")
	for _, link := range fmhyLinks {
		websiteLinkWg.Add(1)
		go func(link string) {
			defer websiteLinkWg.Done()
			links, err := linkscraper.FindWebsiteLinks(link, allocatorCtx)
			if err != nil {
				return
			}
			websiteLinkMu.Lock()
			websiteLinks = append(websiteLinks, links...)
			websiteLinkMu.Unlock()
		}(link)
	}
	websiteLinkWg.Wait()
	var wg sync.WaitGroup
	var mu sync.Mutex
	searchLinks := []linkscraper.SearchInput{}
	validCounts := make(map[string]int)
	invalidCounts := make(map[string]int)
	maxConcurrency := 10
	pool := make(chan struct{}, maxConcurrency)
	for _, link := range websiteLinks {
		wg.Add(1)
		pool <- struct{}{}

		go func(link linkscraper.WebsiteLink) {
			defer func() { <-pool }()
			defer wg.Done()

			searchLink, err := linkscraper.FindSearchInput(link, allocatorCtx)
			mu.Lock()
			defer mu.Unlock()
			if err != nil {
				invalidCounts[link.Category]++
				return
			}
			validCounts[link.Category]++
			searchLinks = append(searchLinks, searchLink)
		}(link)
	}
	wg.Wait()

	results := Results{
		SearchLinks: searchLinks,
	}

	file, err := os.Create("results.json")
	if err != nil {
		println("Error creating JSON file:", err.Error())
		return
	}
	defer file.Close()

	encoder := json.NewEncoder(file)
	encoder.SetIndent("", "  ")
	if err := encoder.Encode(results); err != nil {
		println("Error encoding JSON:", err.Error())
		return
	}

	println("Scraping completed. Results saved to results.json")
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
