package scraper_test

import (
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/internal/scraper"
)

func TestFetchData(t *testing.T) {
	t.Run("Return all links from page, sort out result matching query", func(t *testing.T) {
		testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			io.WriteString(w, `<html>
	<div class="container">
		<div class="card">
			<a href="https://example.com">Example</a>
		</div>
		<div class="card">
			<a href="https://example.com/2">Example 2</a>
		</div>
		<div class="card">
			<a href="https://test.com">Test</a>
		</div>
		<div class="card">
			<a href="https://test.com/2">Test 2</a>
		</div>
		<div class="card">
			<a href="https://wronglink.com">Wrong Link</a>
		</div>
	</div>
</html>`)
		}))
		defer testServer.Close()
		got, _ := scraper.FetchItems(testServer.URL, "example test")
		want := []scraper.Link{
			{"Example", "https://example.com"},
			{"Example 2", "https://example.com/2"},
			{"Test", "https://test.com"},
			{"Test 2", "https://test.com/2"},
		}

		if !reflect.DeepEqual(got, want) {
			t.Errorf("\ngot %q,\nwant %q", got, want)
		}
	})

	t.Run("Return error if request fails", func(t *testing.T) {
		errorCases := map[string]int{
			"403: Forbidden":          http.StatusForbidden,
			"401: Unauthorized":       http.StatusUnauthorized,
			"400: Bad Request":        http.StatusBadRequest,
			"404: Not Found":          http.StatusNotFound,
			"408: Request Timeout":    http.StatusRequestTimeout,
			"405: Method Not Allowed": http.StatusMethodNotAllowed,
			"429: Too Many Requests":  http.StatusTooManyRequests,
		}
		for want, status := range errorCases {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				http.Error(w, want, status)
			}))
			defer testServer.Close()

			_, got := scraper.FetchItems(testServer.URL, "")

			if got.Error() != want {
				t.Errorf("got %q, want %q", got, want)
			}
		}
	})
}
