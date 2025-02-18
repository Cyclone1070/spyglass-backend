package scraper_test

import (
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func TestFetchData(t *testing.T) {
	t.Run("make requests with valid formats to pages", func(t *testing.T) {
		testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			io.WriteString(w, "request body")
		}))
		defer testServer.Close()
		got := scraper.FetchHTML(testServer.URL)
		want := "request body"

		if got != want {
			t.Errorf("got %q, want %q", got, want)
		}
	})
}
