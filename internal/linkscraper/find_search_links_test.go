package linkscraper_test

import (
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper"
	"github.com/google/go-cmp/cmp"
)

func TestFindSearchLinks(t *testing.T) {
	for _, inputType := range []string{"search", "text"} {
		t.Run("return the search URLs if the link has it", func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				fmt.Fprintf(
					w,
					`<form action="/search" method="get">
						<input name="q" type="%s"/>
					</form>`,
					inputType,
				)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()
			want := linkscraper.SearchLink{"test title", testServer.URL + "/search", "test category", "q"}
			assertFindSearchLinksResult(testServer, want, t)
		})
	}
}

func assertFindSearchLinksResult(
	testServer *httptest.Server,
	want linkscraper.SearchLink,
	t *testing.T,
) {
	t.Helper()
	got, _ := linkscraper.FindSearchLinks(linkscraper.WebsiteLink{"test title", testServer.URL, "test category"})
	if diff := cmp.Diff(want, got); diff != "" {
		t.Errorf("FindLinks() mismatch (-want +got):\n%s", diff)
	}
}
