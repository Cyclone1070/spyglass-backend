package jobs

import (
	"encoding/json"
	"io"
)

func UpdateCardPaths(writer io.Writer) error {
	cardPaths := map[string]string{
		"card": "path",
	}
	
	return json.NewEncoder(writer).Encode(cardPaths)
}
