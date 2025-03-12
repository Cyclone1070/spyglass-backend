package jobs_test

import (
	"bytes"
	"encoding/json"
	"testing"

	"github.com/Cyclone1070/spyglass-backend/jobs"
)

func TestUpdateCardPath(t *testing.T) {
	var buffer bytes.Buffer
	var result map[string]string

	jobs.UpdateCardPath(&buffer)
	err := json.NewDecoder(&buffer).Decode(&result)

	if err != nil {
		t.Errorf("Error decoding JSON: %v", err)
	}
}
