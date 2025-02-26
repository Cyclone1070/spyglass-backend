package scraper_test

import (
	"fmt"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func TestMapPageStructure(t *testing.T) {
	t.Run("return the parent container of the cards", func(t *testing.T) {
		testCases := []string{"card", "card2", "cardContainer", "resultCard"}
		for _, phrase := range testCases {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				fmt.Fprintf(w, `<html>
	<div class="container">
		<div class="%s">
			<a href="https://example.com">Example</a>
		</div>
		<div class="%s">
			<a href="https://example.com/2 ">Example 2</a>
		</div>
		<div class="%s">
			<a href="https://test.com">Test</a>
		</div>
		<div class="%s">
			<a href="https://test.com/2">Test 2</a>
		</div>
		<div class="%s">
			<a href="https://wronglink.com">Wrong Link</a>
		</div>
	</div>
</html>`, phrase, phrase, phrase, phrase, phrase)
			}))
			defer testServer.Close()

			got := scraper.FindCardIdentifier(testServer.URL, "example test")
			want := "div." + phrase

			if got != want {
				t.Errorf("got %q, want %q", got, want)
			}
		}
	})
}
