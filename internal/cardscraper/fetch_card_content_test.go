package cardscraper_test

import (
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/cardscraper"
	"github.com/google/go-cmp/cmp"
)

func TestFetchCardContent(t *testing.T) {
	type TestCase struct {
		description string
		top         string
		bottom      string
		cardPath    string
		otherText   []string
		want        []cardscraper.CardContent
	}
	testCases := []TestCase{
		{
			description: "return links based on cardPath",
			top:         "<div class='card'>",
			bottom:      "</div>",
			cardPath:    "html > body > div.container > div.card",
			want: []cardscraper.CardContent{
				{Title: "Example", URL: "https://example.com", OtherText: []string{}},
				{Title: "Example 2", URL: "https://example.com/2", OtherText: []string{}},
				{Title: "Test", URL: "https://test.com", OtherText: []string{}},
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
			want: []cardscraper.CardContent{
				{Title: "Example", URL: "https://example.com", OtherText: []string{"Other Text"}},
				{Title: "Example 2", URL: "https://example.com/2", OtherText: []string{"Other Text 2", "Other Text 3"}},
				{Title: "Test", URL: "https://test.com", OtherText: []string{"Other Text Test"}},
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

			got, err := cardscraper.FetchCardContent(testServer.URL, testCase.cardPath, "example test")

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if diff := cmp.Diff(testCase.want, got); diff != "" {
				t.Errorf("FetchCardContent() mismatch (-want +got):\n%s", diff)
			}
		})
	}
}
