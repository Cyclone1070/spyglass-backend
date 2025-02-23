package main

import (
	"fmt"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func main() {
	links, err := scraper.FetchItems("https://www.imdb.com/find/?s=tt&q=test&ref_=nv_sr_sm", "test")
	if err == nil {
		for _, link := range links {
			fmt.Printf("%s: %s\n", link.Title, link.Url)
		}
	} else {
		fmt.Println(err)
	}
}
