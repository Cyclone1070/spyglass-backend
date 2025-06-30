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

type FindLinksTestCase struct {
	description string
	selectors   []string
	category    string
}

func TestFindLinks(t *testing.T) {
	testCases := []FindLinksTestCase{
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
		t.Run("Scrape all links inside and outside strong tags: "+testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				for _, id := range testCase.selectors {
					fmt.Fprintf(
						w,
						`<h2 id="%[1]s">%[1]s</h2>
						<ul>
							<li class="starred">
								<strong><a href="https://link1-strong%[1]s.com">Link 1 strong %[1]s</a></strong>, 
								- Test
							</li>
							<li> 
								<a href="https://link1%[1]s.com">Link 1 %[1]s</a>, 
								- Test
							</li>
							<li class="starred">
								<strong><a href="https://link2-strong%[1]s.com">Link 2 strong %[1]s</a></strong>, 
								<a href="https://link2%[1]s.com">Link 2 %[1]s</a>, 
								- Test
							</li>
						</ul>`,
						id,
					)
				}
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			want := []linkscraper.Link{}
			for _, id := range testCase.selectors {
				want = append(want,
					linkscraper.Link{"Link 1 strong " + id, "https://link1-strong" + id + ".com", testCase.category},
					linkscraper.Link{"Link 1 " + id, "https://link1" + id + ".com", testCase.category},
					linkscraper.Link{"Link 2 strong " + id, "https://link2-strong" + id + ".com", testCase.category},
					linkscraper.Link{"Link 2 " + id, "https://link2" + id + ".com", testCase.category},
				)
			}

			assertResult(testServer, want, t)
		})
		t.Run("Skip when links titles are number (mirrors): "+testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(
					w,
					`<h2 id="ebooks">Skip mirror</h2>
					<ul>
						<li class="starred">
							<a href="https://link1-mirror.com">2</a>, 
						</li>
					</ul>`,
				)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			want := []linkscraper.Link{}
			assertResult(testServer, want, t)
		})
		t.Run("Skip when globe icon is present: "+testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				io.WriteString(
					w,
					`<h2 id="ebooks">Skip globe</h2>
					<ul>
						<li class="starred">
							<span class="i-twemoji-globe-with-meridians"></span>
							<strong><a href="https://link1.com">Link to skip</a></strong>, 
						</li>
					</ul>`,
				)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			want := []linkscraper.Link{}
			assertResult(testServer, want, t)
		})
		t.Run("Skip when link contains skip keywords: "+testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				for _, skipKeyword := range linkscraper.SkipKeywords {
					fmt.Fprintf(
						w,
						`<h2 id="ebooks">Skip links</h2>
						<ul>
							<li class="starred">
								<a href="https://%[1]s.com">Keyword in link URL </a>, 
								<a href="https://skiplink.com">Keyword in link text %[1]s</a>, 
							</li>
							<li class="starred">
								<a href="https://skiplink.com">Keyword in link description </a>, 
								- %[1]s
							</li>
						</ul>`,
						skipKeyword,
					)
				}
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			want := []linkscraper.Link{}
			assertResult(testServer, want, t)
		})
	}
}

func assertResult(testServer *httptest.Server, want []linkscraper.Link, t *testing.T) {
	got, err := linkscraper.FindLinks(testServer.URL)

	if err != nil {
		t.Errorf("unexpected error: %v", err)
	} else if diff := cmp.Diff(want, got); diff != "" {
		t.Errorf("FindLinks() mismatch (-want +got):\n%s", diff)
	}
}
