package jobs

import (
	"encoding/json"
	"io"
)

func UpdateCardPath(writer io.Writer) {
	cardPaths := map[string]string{
		"card": "path",
	}
	json.NewEncoder(writer).Encode(cardPaths)
}
