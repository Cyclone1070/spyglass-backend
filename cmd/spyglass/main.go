package main

import (
	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func main() {
	println(scraper.FetchHTML("http://quotes.toscrape.com/page/1/"))
}
