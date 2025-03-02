package scraper_test

import (
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func TestFindCardPath(t *testing.T) {
	testCases := []map[string]string{
		{
			"description": "happy path, card is the direct parent of the link",
			"top":         "<div class='card'>",
			"bottom":      "</div>",
			"want":        "html > body > div.container > div.card",
		},
		{
			"description": "card is the grandparent of the link",
			"top":         "<div class='cardGrandParent'><div class='insideWrapper'>",
			"bottom":      "</div></div>",
			"want":        "html > body > div.container > div.cardGrandParent",
		},
		{
			"description": "card has multiple class names",
			"top":         "<div class='card2 card3'>",
			"bottom":      "</div>",
			"want":        "html > body > div.container > div.card2.card3",
		},
		{
			"description": "card is the <a> tag itself",
			"top":         "",
			"bottom":      "",
			"want":        "html > body > div.container > a",
		},
		{
			"description": "if multiple paths found, return the most common one",
			"prefix": `
<div class='wrongCard'>
	<a href='https://wrongcard.com'>Wrong Card Example</a>
</div>
<div class='wrongCard2'>
	<a href='https://wrongcard2.com'>Wrong Card 2 Example</a>
</div>`,
			"top":    "<div class='card'>",
			"bottom": "</div>",
			"want":   "html > body > div.container > div.card",
		},
	}
	links := []string{"<a href='https://example.com'>Example</a>", "<a href='https://example.com/2'>Example 2</a>", "<a href='https://test.com'>Test</a>"}

	for _, testCase := range testCases {
		t.Run(testCase["description"], func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				fmt.Fprintf(w, "%s", testCase["prefix"])
				io.WriteString(w, "<div class='container'>")

				for _, link := range links {
					fmt.Fprintf(w, "%s", testCase["top"])
					io.WriteString(w, link)
					fmt.Fprintf(w, "%s", testCase["bottom"])
				}

				io.WriteString(w, "</div>")
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			got, err := scraper.FindCardPath(testServer.URL, "example test")

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if got != testCase["want"] {
				t.Errorf("got %q, want %q", got, testCase["want"])
			}
		})
	}
	// error tests
	httpErrorCases := map[string]int{
		"Forbidden":          http.StatusForbidden,
		"Unauthorized":       http.StatusUnauthorized,
		"Bad Request":        http.StatusBadRequest,
		"Not Found":          http.StatusNotFound,
		"Request Timeout":    http.StatusRequestTimeout,
		"Method Not Allowed": http.StatusMethodNotAllowed,
		"Too Many Requests":  http.StatusTooManyRequests,
	}
	for wantMessage, status := range httpErrorCases {
		t.Run(wantMessage, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				http.Error(w, wantMessage, status)
			}))
			defer testServer.Close()

			_, got := scraper.FindCardPath(testServer.URL, "")

			if got == nil {
				t.Errorf("got no error, want %q", wantMessage)
			} else if errors.Is(got, errors.New(wantMessage)) {
				t.Errorf("got %q, want %q", got, wantMessage)
			}
		})
	}
	parsingErrorCases := map[string]string{
		"multiple paths with the same occurence counts": `
<div class='card'>
	<a href='https://example.com'>Example Test</a>
</div>
<a href='https://example.com/2'>Example 2 Test</a>`,
		"no card matches the query": ``,
	}
	for wantMessage, response := range parsingErrorCases {
		t.Run(wantMessage, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(w, response)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			_, got := scraper.FindCardPath(testServer.URL, "test")

			if got == nil {
				t.Errorf("got no error, want %q", wantMessage)
			} else if got.Error() != wantMessage {
				t.Errorf("got %q, want %q", got, wantMessage)
			}
		})
	}
}
