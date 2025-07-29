package cardscraper

// CardContent represents the structured data extracted from a single "result card"
// on a search results page. It holds the primary information about a search result.
type CardContent struct {
	// Title is the main, clickable text of the result, usually from an anchor tag.
	Title string
	// URL is the destination link for the result.
	URL string
	// OtherText contains all other text fragments found within the card.
	// This is gathered by finding all leaf nodes in the card's DOM structure,
	// providing additional, unstructured context about the result.
	OtherText []string
}
