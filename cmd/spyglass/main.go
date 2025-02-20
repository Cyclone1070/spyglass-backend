package main

import (
	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func main() {
	println(scraper.FetchHTML("https://www.imdb.com/find/?s=tt&q=test&ref_=nv_sr_sm"))
}
