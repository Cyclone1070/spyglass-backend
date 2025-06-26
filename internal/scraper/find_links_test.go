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

func TestFindLinks(t *testing.T) {
	testCases := []struct {
		description string
		ids          []string
		catagory    string
	}{
		{
			"list of ebooks catagory",
			[]string{"ebooks", "public-domain", "pdf-search"},
			"Books",
		},
	}

	for _, testCase := range testCases {
		t.Run(testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				for _, id := range testCase.ids {
					fmt.Fprintf(
						w,
						`<h2 id="%s">%s</h2>
						<ul>
							<li class="starred">
								<strong><a href="https://link1%s.com">Link 1 %s</a></strong>, 
								<a href="https://link1.com">2</a>, 
								<a href="https://link1.com">3</a> 
							</li>
							<li class="starred">
								<strong><a href="https://link2%s.com">Link 2 %s</a></strong>, 
								<a href="https://link1.com">2</a>, 
								<a href="https://link1.com">3</a> 
							</li>
						</ul>`,
						id, id, id, id, id, id,
					)
				}
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			got, err := scraper.FindLinks(testServer.URL)

			want := []scraper.Link{}
			for _, id := range testCase.ids {
				want = append(want, scraper.Link{"Link 1 " + id, "https://link1" + id + ".com", testCase.catagory}, scraper.Link{"Link 2 " + id, "https://link2" + id + ".com", testCase.catagory})	
			}

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if diff := cmp.Diff(want, got); diff != "" {
				t.Errorf("FindLinks() mismatch (-want +got):\n%s", diff)
			}
		})
	}
}
