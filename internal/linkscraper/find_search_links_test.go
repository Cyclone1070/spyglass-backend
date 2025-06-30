package linkscraper_test

import (
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/linkscraper"
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
			got, _ := linkscraper.FindSearchLinks(linkscraper.WebsiteLink{"test title", testServer.URL, "test category"})
			assertEqual(want, got, t)
		})
		t.Run("return error when the link does not have a valid get request search form", func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(
					w,
					`<form action="/search" method="post">
						<input name="q" type="text"/>
					</form>`,
				)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()
			want := errors.New("no search URL found in link: " + testServer.URL)
			_, err := linkscraper.FindSearchLinks(linkscraper.WebsiteLink{"test title", testServer.URL, "test category"})
			if err == nil {
				t.Fatalf("expected error %q, got no error", want)
			} else {
				assertEqual(want.Error(), err.Error(), t)
			}
		})
		t.Run("return error when multiple search urls are found", func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(
					w,
					`<form action="/search1" method="get">
						<input name="q1" type="search"/>
					</form>
					<form action="/search2" method="get">
						<input name="q2" type="search"/>
					</form>`,
				)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()
			want := errors.New("multiple search URLs found in link: " + testServer.URL)
			_, err := linkscraper.FindSearchLinks(linkscraper.WebsiteLink{"test title", testServer.URL, "test category"})
			if err == nil {
				t.Fatalf("expected error %q, got no error", want)
			} else {
				assertEqual(want.Error(), err.Error(), t)
			}
		})
		t.Run("return error when search form has multiple valid input elements", func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(
					w,
					`<form action="/search" method="get">
						<input name="q1" type="search"/>
						<input name="q2" type="text"/>
					</form>`,
				)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()
			want := errors.New("multiple potential search params found in link: " + testServer.URL)
			_, err := linkscraper.FindSearchLinks(linkscraper.WebsiteLink{"test title", testServer.URL, "test category"})
			if err == nil {
				t.Fatalf("expected error %q, got no error", want)
			} else {
				assertEqual(want.Error(), err.Error(), t)
			}
		})
	}
}
