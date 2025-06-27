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
		selectors   []string
		category    string
	}{
		{
			"list of ebooks category",
			[]string{"ebooks", "public-domain", "pdf-search"},
			"Books",
		},
		{
			"list of movies category",
			[]string{"streaming-sites", "free-w-ads", "anime-streaming"},
			"Movies",
		},
		{
			"list of games download category",
			[]string{"download-games"},
			"Games Download",
		},
		{
			"list of games repack category",
			[]string{"repack-games"},
			"Games Repack",
		},
		{
			"list of abandonware/ROM category",
			[]string{"abandonware-retro", "rom-sites", "nintendo-roms", "sony-roms"},
			"Abandonware/ROM",
		},
		{
			"list of mac games category",
			[]string{"mac-gaming"},
			"Mac Games",
		},
		{
			"list of linux games category",
			[]string{"linux-gaming"},
			"Linux Games",
		},
		{
			"list of windows software category",
			[]string{"software-sites"},
			"Windows Software",
		},
		{
			"list of mac software category",
			[]string{"software-sites-1"},
			"Mac Software",
		},
		{
			"list of android apps category",
			[]string{"modded-apks", "untouched-apks"},
			"Android apps",
		},
		{
			"list of ios apps category",
			[]string{"ios-ipas"},
			"IOS apps",
		},
	}

	for _, testCase := range testCases {
		t.Run(testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				for _, id := range testCase.selectors {
					fmt.Fprintf(
						w,
						`<h2 id="%[1]s">%[1]s</h2>
						<ul>
							<li class="starred">
								<strong><a href="https://link1%[1]s.com">Link 1 %[1]s</a></strong>, 
								<a href="https://link1.com">2</a>, 
								<a href="https://link1.com">3</a> 
							</li>
							<li class="starred">
								<strong><a href="https://link2%[1]s.com">Link 2 %[1]s</a></strong>, 
								<a href="https://link1.com">2</a>, 
								<a href="https://link1.com">3</a> 
							</li>
						</ul>`,
						id,
					)
				}
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			got, err := scraper.FindLinks(testServer.URL)

			want := []scraper.Link{}
			for _, id := range testCase.selectors {
				want = append(want, scraper.Link{"Link 1 " + id, "https://link1" + id + ".com", testCase.category}, scraper.Link{"Link 2 " + id, "https://link2" + id + ".com", testCase.category})
			}

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if diff := cmp.Diff(want, got); diff != "" {
				t.Errorf("FindLinks() mismatch (-want +got):\n%s", diff)
			}
		})
	}
}
