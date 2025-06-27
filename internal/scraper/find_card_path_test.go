package scraper_test

import (
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
	"github.com/google/go-cmp/cmp"
)

func TestFindCardPath(t *testing.T) {
	testCases := []struct {
		description string
		top         string
		bottom      string
		want        string
		prefix      string
	}{
		{
			"happy path, card is the direct parent of the link",
			"<div class='card'>",
			"</div>",
			"html > body > div.container > div.card",
			"",
		},
		{
			"card is the grandparent of the link",
			"<div class='cardGrandParent'><div class='insideWrapper'>",
			"</div></div>",
			"html > body > div.container > div.cardGrandParent",
			"",
		},
		{
			"card has multiple class names",
			"<div class='card2 card3'>",
			"</div>",
			"html > body > div.container > div.card2.card3",
			"",
		},
		{
			"card is the <a> tag itself",
			"",
			"",
			"html > body > div.container > a",
			"",
		},
		{
			"if multiple paths found, return the most common one",
			"<div class='card'>",
			"</div>",
			"html > body > div.container > div.card",
			`<div class='wrongCard'>
				<a href='https://wrongcard.com'>Wrong Card Example</a>
			</div>
			<div class='wrongCard2'>
				<a href='https://wrongcard2.com'>Wrong Card 2 Example</a>
			</div>`,
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
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				fmt.Fprintf(w, "%s", testCase.prefix)
				io.WriteString(w, "<div class='container'>")

				for _, link := range links {
					io.WriteString(w, testCase.top)
					io.WriteString(w, link)
					io.WriteString(w, testCase.bottom)
				}

				io.WriteString(w, "</div>")
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			got, err := scraper.FindCardPath(testServer.URL, "example test")

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if diff := cmp.Diff(testCase.want, got); diff != "" {
				t.Errorf("FindCardPath() mismatch (-want +got):\n%s", diff)
			}
		})
	}
}

func TestFindCardPathParsingErrors(t *testing.T) {
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

			assertError(t, got, wantMessage)
		})
	}
}

func assertError(t testing.TB, got error, wantMessage string) {
	t.Helper()
	if got == nil {
		t.Errorf("got no error, want %q", wantMessage)
	} else if got.Error() != wantMessage {
		t.Errorf("got %q, want %q", got, wantMessage)
	}
}
