package jobs_test

import (
	"bytes"
	"encoding/json"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/jobs"
)

func TestUpdateCardPaths(t *testing.T) {
	// servers
	t.Run("return valid JSON, no http or writer error", func(t *testing.T) {
		var buffer bytes.Buffer
		var result map[string]string

		writeErr := jobs.UpdateCardPaths(&buffer)
		if writeErr != nil {
			t.Errorf("Error updating card path: %v", writeErr)
		}

		decodeErr := json.NewDecoder(&buffer).Decode(&result)
		if decodeErr != nil {
			t.Errorf("Error decoding JSON: %v", decodeErr)
		}
	})
}
