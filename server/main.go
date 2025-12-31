package main

import (
	"log"
	"net/http"
	"os"
	"time"
)

func main() {
	turnTokenStore := NewTurnTokenStore(5 * time.Minute)

	// Initialize signaling
	hub := newHub(turnTokenStore)
	go hub.run()

	// Simple CORS middleware for API
	enableCors := func(h http.HandlerFunc) http.HandlerFunc {
		return func(w http.ResponseWriter, r *http.Request) {
			if !isOriginAllowed(r) {
				http.Error(w, "Forbidden", http.StatusForbidden)
				return
			}
			origin := r.Header.Get("Origin")
			if origin != "" {
				w.Header().Set("Access-Control-Allow-Origin", origin)
				w.Header().Set("Vary", "Origin")
			}
			if r.Method == "OPTIONS" {
				w.Header().Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
				w.Header().Set("Access-Control-Allow-Headers", "Content-Type, X-Turn-Token")
				w.WriteHeader(http.StatusNoContent)
				return
			}
			h(w, r)
		}
	}

	// Rate Limiters
	// WS: 10 connections per minute per IP
	wsLimiter := NewIPLimiter(10.0/60.0, 5)

	// API: 10 requests per minute per IP
	apiLimiter := NewIPLimiter(10.0/60.0, 5)

	http.HandleFunc("/ws", rateLimitMiddleware(wsLimiter, func(w http.ResponseWriter, r *http.Request) {
		serveWs(hub, w, r)
	}))

	http.HandleFunc("/api/turn-credentials", rateLimitMiddleware(apiLimiter, enableCors(handleTurnCredentials(turnTokenStore))))

	port := os.Getenv("PORT")
	if port == "" {
		port = "8080"
	}

	log.Printf("Server executing on :%s", port)
	server := &http.Server{
		Addr:              ":" + port,
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       15 * time.Second,
		WriteTimeout:      15 * time.Second,
		IdleTimeout:       60 * time.Second,
	}
	if err := server.ListenAndServe(); err != nil {
		log.Fatal("ListenAndServe: ", err)
	}
}
