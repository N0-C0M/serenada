package main

import (
	"crypto/hmac"
	"crypto/rand"
	"crypto/sha1"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"net/http"
	"os"
	"sync"
	"time"
)

type TurnConfig struct {
	Username string   `json:"username"`
	Password string   `json:"password"`
	URIs     []string `json:"uris"`
	TTL      int      `json:"ttl"`
}

type turnToken struct {
	ip      string
	expires time.Time
}

type TurnTokenStore struct {
	mu     sync.Mutex
	tokens map[string]turnToken
	ttl    time.Duration
}

func NewTurnTokenStore(ttl time.Duration) *TurnTokenStore {
	return &TurnTokenStore{
		tokens: make(map[string]turnToken),
		ttl:    ttl,
	}
}

func (s *TurnTokenStore) Issue(ip string) (string, time.Time) {
	b := make([]byte, 16)
	rand.Read(b)
	token := hex.EncodeToString(b)
	expires := time.Now().Add(s.ttl)

	s.mu.Lock()
	s.tokens[token] = turnToken{ip: ip, expires: expires}
	s.mu.Unlock()

	return token, expires
}

func (s *TurnTokenStore) Validate(token, ip string) bool {
	if token == "" {
		return false
	}
	now := time.Now()

	s.mu.Lock()
	defer s.mu.Unlock()

	entry, ok := s.tokens[token]
	if !ok {
		return false
	}
	if now.After(entry.expires) {
		delete(s.tokens, token)
		return false
	}
	if entry.ip != "" && entry.ip != ip {
		return false
	}
	return true
}

func handleTurnCredentials(store *TurnTokenStore) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet {
			http.Error(w, "Method Not Allowed", http.StatusMethodNotAllowed)
			return
		}

		if store == nil {
			http.Error(w, "TURN token store unavailable", http.StatusServiceUnavailable)
			return
		}

		token := r.Header.Get("X-Turn-Token")
		if token == "" {
			token = r.URL.Query().Get("token")
		}
		if !store.Validate(token, getClientIP(r)) {
			http.Error(w, "Unauthorized", http.StatusUnauthorized)
			return
		}

		// 1. Get Secret and Host from Env
		secret := os.Getenv("TURN_SECRET")
		host := os.Getenv("TURN_HOST")
		if secret == "" || host == "" {
			http.Error(w, "TURN not configured", http.StatusServiceUnavailable)
			return
		}

		// 2. Generate Credentials (Time-limited)
		// Standard TURN REST API: username = timestamp:user
		ttl := 15 * 60 // 15 minutes
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
}
