// Package utils provide utilities functions
package utils

import (
	"time"

	"github.com/gocolly/colly/v2"
	"github.com/gocolly/colly/v2/extensions"
)

func ConfiguredCollector() *colly.Collector {
	// Create a new collector with the default configuration
	collector := colly.NewCollector()

	// --- Step 2: Configure Behavior to Mimic a Human ---
	// Set a generous timeout. Some sites can be slow.
	collector.SetRequestTimeout(10 * time.Second)

	// CRITICAL: Set realistic rate limits. Bots are fast; humans are not.
	// This tells the scraper to wait a random time between 1 and 3 seconds
	// between requests to the same domain and only run 2 scrapers in parallel.
	collector.Limit(&colly.LimitRule{
		DomainGlob:  "*",
		RandomDelay: 2 * time.Second,
	})

	// --- Step 3: Configure Identity with Realistic Browser Headers ---
	// Use the RandomUserAgent and Referer extensions for convenience.
	// This automatically sets a plausible, changing User-Agent and the Referer header on each request.
	extensions.RandomUserAgent(collector)
	extensions.Referer(collector)

	// For ultimate control, you can define a static, perfect set of headers.
	// This is often better than randomizing because it's more consistent.
	// The order of headers can matter to some anti-bot systems.
	collector.OnRequest(func(r *colly.Request) {
		r.Headers.Set("Accept-Language", "en-US,en;q=0.9")

		// NOTE: Disabling compresssion, should enable it if more camouflage is needed. Will need to look up how to handle decompression in the response.

		// r.Headers.Set("Accept-Encoding", "gzip, deflate, br")

		r.Headers.Set("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7")
		r.Headers.Set("Sec-Ch-Ua", `"Not/A)Brand";v="99", "Google Chrome";v="123", "Chromium";v="123"`)
		r.Headers.Set("Sec-Ch-Ua-Mobile", "?0")
		r.Headers.Set("Sec-Ch-Ua-Platform", `"macOS"`)
		r.Headers.Set("Sec-Fetch-Dest", "document")
		r.Headers.Set("Sec-Fetch-Mode", "navigate")
		r.Headers.Set("Sec-Fetch-Site", "same-origin")
		r.Headers.Set("Sec-Fetch-User", "?1")
		r.Headers.Set("Upgrade-Insecure-Requests", "1")
	})

	return collector
}
