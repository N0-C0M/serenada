package main

import (
	"crypto/hmac"
	"crypto/sha1"
	"encoding/base64"
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"time"
)

type TurnConfig struct {
	Username string   `json:"username"`
	Password string   `json:"password"`
	URIs     []string `json:"uris"`
	TTL      int      `json:"ttl"`
}

func handleTurnCredentials(w http.ResponseWriter, r *http.Request) {
	// 1. Get Secret and Host from Env
	secret := os.Getenv("TURN_SECRET")
	host := os.Getenv("TURN_HOST")
	if secret == "" || host == "" {
		// If not configured, return empty config or error (for dev we might skip content)
		// For now let's just error if it's critical, or return empty list.
		// Usually in dev we might rely on public STUN.
		if host == "" {
			host = "stun:stun.l.google.com:19302" // Fallback to public for dev
			json.NewEncoder(w).Encode(TurnConfig{
				URIs: []string{host},
			})
			return
		}
	}

	// 2. Generate Credentials (Time-limited)
	// Standard TURN REST API: username = timestamp:user
	ttl := 24 * 3600 // 24 hours
	timestamp := time.Now().Unix() + int64(ttl)
	username := fmt.Sprintf("%d:connected-user", timestamp)

	// Password = HMAC-SHA1(secret, username)
	mac := hmac.New(sha1.New, []byte(secret))
	mac.Write([]byte(username))
	password := base64.StdEncoding.EncodeToString(mac.Sum(nil))

	config := TurnConfig{
		Username: username,
		Password: password,
		URIs: []string{
			"stun:" + host,
			"turn:" + host,
		},
		TTL: ttl,
	}

	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(config)
}
