package jobs_test

import (
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/jobs"
)

func TestUpdateLinks(t *testing.T) {
	testCases := []struct {
		description string
		content     string
		want        map[string]string
	}{
		{
			"simple list of starred links",
			`
			<h2 id="ebooks">Ebooks</h2>
			<ul>
				<li class="starred">
					<strong><a href="https://link1.com">Link 1 main</a></strong>, 
					<a href="https://link1.com">2</a>, 
					<a href="https://link1.com">3</a> 
				</li>
				<li class="starred">
					<strong><a href="https://link2.com">Link 2 main</a></strong>, 
					<a href="https://link1.com">2</a>, 
					<a href="https://link1.com">3</a> 
				</li>
			</ul>
			`,
			map[string]string{"Link 1 main": "https://link1.com", "Link 2 main": "https://link2.com"},
		},
	}

	for _, testCase := range testCases {
		t.Run(testCase.description, func(t *testing.T) {
			testServer := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				io.WriteString(w, "<html><body>")
				fmt.Fprintf(w, "%s", testCase.content)
				io.WriteString(w, "</body></html>")
			}))
			defer testServer.Close()

			got, err := jobs.UpdateLinks(testServer.URL)

			if err != nil {
				t.Errorf("unexpected error: %v", err)
			} else if !reflect.DeepEqual(got, testCase.want) {
				t.Errorf("got %q, want %q", got, testCase.want)
			}
		})
	}
}
