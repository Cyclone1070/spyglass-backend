package main

import (
	"io"
	"log"
	"net/http"
)

func handleHome(w http.ResponseWriter, r *http.Request) {
	io.WriteString(w, "Welcome to my server!")
}
func main() {
	http.HandleFunc("/", handleHome)
	err := http.ListenAndServe(":8080", nil)
	if err != nil {
		log.Fatal(err)
	}
}
