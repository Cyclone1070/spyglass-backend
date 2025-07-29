package main

import (
	"encoding/json"
	"fmt"
	"os"
	"sync"

	"github.com/Cyclone1070/spyglass-backend/internal/cardscraper"
	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper"
)

func main() {
	// high level settings
	maxConcurrency := 50

	// files
	file, err := os.Open("searchLinks.json")
	if err != nil {
		println("Error opening JSON file:", err.Error())
		return
	}
	defer file.Close()
	decoder := json.NewDecoder(file)
	var searchLinks []linkscraper.SearchLink
	decodeErr := decoder.Decode(&searchLinks)
	if decodeErr != nil {
		println("Error decoding JSON:", decodeErr.Error())
		return
	}

	// Category counting
	categoryCounts := make(map[string]int)
	for _, link := range searchLinks {
		categoryCounts[link.Category]++
	}

	// concurrency setup
	var wg sync.WaitGroup
	var mu sync.Mutex // Mutex to protect shared state
	pool := make(chan struct{}, maxConcurrency)
	resultSelectors := []cardscraper.ResultCardSelector{}
	processedCount := 0
	validCounts := make(map[string]int)
	invalidCounts := make(map[string]int)
	totalLinks := len(searchLinks)

	println("Processing links...")
	for _, link := range searchLinks {
		wg.Add(1)
		pool <- struct{}{}
		go func(link linkscraper.SearchLink) {
			defer wg.Done()
			defer func() { <-pool }()

			// Find the search link using the master collector
			foundLink, err := cardscraper.FindResultCardSelector(link)
			mu.Lock()
			defer mu.Unlock()
			processedCount++
			if err != nil {
				invalidCounts[link.Category]++
				fmt.Printf("\nError processing %s: %s\n", link.SearchURL, err.Error())
			} else {
				validCounts[link.Category]++
				resultSelectors = append(resultSelectors, foundLink)
			}
			fmt.Printf("\rProcessed %d/%d links", processedCount, totalLinks)
		}(link)
	}

	wg.Wait()
	fmt.Println()
	// Write the results to a JSON file
	outputFile, err := os.Create("resultCardSelectors.json")
	if err != nil {
		println("Error creating output file:", err.Error())
		return
	}
	defer outputFile.Close()
	encoder := json.NewEncoder(outputFile)
	encoder.SetIndent("", "  ") // Pretty print the JSON
	if err := encoder.Encode(resultSelectors); err != nil {
		println("Error encoding JSON:", err.Error())
		return
	}

	println("\n--- Processing Complete ---")
	totalValid := 0
	totalInvalid := 0
	for category, total := range categoryCounts {
		valid := validCounts[category]
		invalid := invalidCounts[category]
		totalValid += valid
		totalInvalid += invalid
		fmt.Printf("\nCategory: %s (%d links)\n", category, total)
		fmt.Printf("  Valid: %d\n", valid)
		fmt.Printf("  Invalid: %d\n", invalid)
	}
	fmt.Printf("\n--- Overall Summary ---\n")
	fmt.Printf("Total Links Processed: %d\n", totalLinks)
	fmt.Printf("Total Valid Links: %d\n", totalValid)
	fmt.Printf("Total Invalid Links: %d\n", totalInvalid)
}
