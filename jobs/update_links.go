package jobs

import "github.com/gocolly/colly/v2"

func UpdateLinks(url string) (map[string]string, error) {
	collector := colly.NewCollector()
	var err error
	links := make(map[string]string)

	collector.OnHTML("li.starred strong a", func(e *colly.HTMLElement) {
		// Get the main link text and URL
		mainLinkText := e.Text
		mainLinkURL := e.Attr("href")

		// Add the main link to the map
		if mainLinkText != "" && mainLinkURL != "" {
			links[mainLinkText] = mainLinkURL
		}
	})

	collector.OnError(func(r *colly.Response, e error) {
		err = e
	})

	collector.Visit(url)
	
	return links, err
}
