package main

import (
	"fmt"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func main() {
	pattern := scraper.FindCardIdentifier("https://www.imdb.com/find/?s=tt&q=test&ref_=nv_sr_sm", "test")
	fmt.Println("\n" + pattern + "\n")
}
