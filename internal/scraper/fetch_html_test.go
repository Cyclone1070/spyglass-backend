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
		want := `<head></head>
<body>content</body>`
		testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			io.WriteString(w, want)
		}))
		defer testServer.Close()

		got, _ := scraper.FetchHTML(testServer.URL)

		if got != want {
			t.Errorf("got %q, want %q", got, want)
		}
	})
	errorMap := map[string]int{
		"403: Forbidden":          http.StatusForbidden,
		"401: Unauthorized":       http.StatusUnauthorized,
		"400: Bad Request":        http.StatusBadRequest,
		"404: Not Found":          http.StatusNotFound,
		"405: Method Not Allowed": http.StatusMethodNotAllowed,
		"408: Request Timeout":    http.StatusRequestTimeout,
		"429: Too Many Requests":  http.StatusTooManyRequests,
	}
	t.Run("Return error if request fails", func(t *testing.T) {
		for want, status := range errorMap {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				http.Error(w, want, status)
			}))
			defer testServer.Close()

			_, got := scraper.FetchHTML(testServer.URL)

			if got.Error() != want {
				t.Errorf("got %q, want %q", got, want)
			}
		}
	})
}
