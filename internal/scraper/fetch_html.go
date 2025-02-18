package scraper

import (
	"io"
	"net/http"
)

func FetchHTML(url string) string {
	response, _ := http.Get(url)
	defer response.Body.Close()
	bodyByte, _ := io.ReadAll(response.Body)
	bodyString := string(bodyByte)
	return bodyString
}
