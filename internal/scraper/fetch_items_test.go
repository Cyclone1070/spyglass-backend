package scraper_test

import (
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func TestFetchItems(t *testing.T) {
	type TestCase struct {
		description string
		top         string
		bottom      string
		cardPath    string
		want        []scraper.CardContent
	}
	testCases := []TestCase{
		{
			description: "return links based on cardPath",
			top:         "<div class='card'>",
			bottom:      "</div>",
			cardPath:    "html > body > div.container > div.card",
			want: []scraper.CardContent{
				{Title: "Example", Url: "https://example.com", OtherText: []string{}},
				{Title: "Example 2", Url: "https://example.com/2", OtherText: []string{}},
				{Title: "Test", Url: "https://test.com", OtherText: []string{}},
			},
		},
	}
	links := []string{"<a href='https://example.com'>Example</a>", "<a href='https://example.com/2'>Example 2</a>", "<a href='https://test.com'>Test</a>"}

	for _, testCase := range testCases {
		t.Run(testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(w, "<div class='container'>")

				for _, link := range links {
					fmt.Fprintf(w, "%s", testCase.top)
					io.WriteString(w, link)
					fmt.Fprintf(w, "%s", testCase.bottom)
				}

				io.WriteString(w, "</div>")
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			got, err := scraper.FetchItems(testServer.URL, testCase.cardPath, "example test")

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if !reflect.DeepEqual(got, testCase.want) {
				t.Errorf("\ngot:\n%q\nwant:\n%q", got, testCase.want)
			}
		})
	}
	t.Run("Return error if request fails", func(t *testing.T) {
		errorCases := map[string]int{
			"Forbidden":          http.StatusForbidden,
			"Unauthorized":       http.StatusUnauthorized,
			"Bad Request":        http.StatusBadRequest,
			"Not Found":          http.StatusNotFound,
			"Request Timeout":    http.StatusRequestTimeout,
			"Method Not Allowed": http.StatusMethodNotAllowed,
			"Too Many Requests":  http.StatusTooManyRequests,
		}
		for want, status := range errorCases {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				http.Error(w, want, status)
			}))
			defer testServer.Close()

			_, got := scraper.FetchItems(testServer.URL, "", "")

			if got.Error() != want {
				t.Errorf("got %q, want %q", got, want)
			}
		}
	})
}
