package scraper_test

import (
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func TestFetchCardContent(t *testing.T) {
	type TestCase struct {
		description string
		top         string
		bottom      string
		cardPath    string
		otherText   []string
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
		{
			description: "return other text based on cardPath",
			top:         "<div class='card'>",
			bottom:      "</div>",
			cardPath:    "html > body > div.container > div.card",
			otherText: []string{
				"<p>Other Text</p>",
				"<p>Other Text 2</p><p>Other Text 3</p>",
				"<p>Other Text Test</p>",
			},
			want: []scraper.CardContent{
				{Title: "Example", Url: "https://example.com", OtherText: []string{"Other Text"}},
				{Title: "Example 2", Url: "https://example.com/2", OtherText: []string{"Other Text 2", "Other Text 3"}},
				{Title: "Test", Url: "https://test.com", OtherText: []string{"Other Text Test"}},
			},
		},
	}
	// links to be found in the html response
	links := []string{
		"<a href='https://example.com'>Example</a>",
		"<a href='https://example.com/2'>Example 2</a>",
		"<a href='https://test.com'>Test</a>",
	}

	for _, testCase := range testCases {
		t.Run(testCase.description, func(t *testing.T) {
			// write html response
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(w, "<div class='container'>")

				for i, link := range links {
					io.WriteString(w, testCase.top)
					if len(testCase.otherText) > i {
						io.WriteString(w, testCase.otherText[i])
					}
					io.WriteString(w, link)
					io.WriteString(w, testCase.bottom)
				}

				io.WriteString(w, "</div>")
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			got, err := scraper.FetchCardContent(testServer.URL, testCase.cardPath, "example test")

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if !reflect.DeepEqual(got, testCase.want) {
				t.Errorf("\ngot:\n%q\nwant:\n%q", got, testCase.want)
			}
		})
	}
}
func TestFetchErrors(t *testing.T) {
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
		t.Run(want, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				http.Error(w, want, status)
			}))
			defer testServer.Close()

			_, got := scraper.FetchCardContent(testServer.URL, "", "")

			if got == nil {
				t.Errorf("got no error, want %q", want)
			} else if got.Error() != want {
				t.Errorf("got %q, want %q", got, want)
			}
		})
	}
}
