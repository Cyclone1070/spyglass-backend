package linkscraper

type WebsiteLink struct {
	Title    string
	URL      string
	Category string
	Starred  bool
}
type SearchInput struct {
	WebsiteLink
	InputSelector string
	Method        string
}
