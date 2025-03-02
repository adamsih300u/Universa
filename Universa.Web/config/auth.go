package config

import (
	"crypto/subtle"
	"log"
	"net/http"
	"os"
)

type AuthConfig struct {
	Username string
	Password string
}

var Auth AuthConfig

func InitAuth() {
	// Debug: Print current working directory
	if dir, err := os.Getwd(); err == nil {
		log.Printf("DEBUG: Current working directory: %s", dir)
	}

	// Debug: Print all environment variables
	log.Printf("DEBUG: All environment variables:")
	for _, env := range os.Environ() {
		log.Printf("DEBUG: %s", env)
	}

	// Debug: Print specific environment variables
	log.Printf("DEBUG: AUTH_USERNAME direct read: '%s'", os.Getenv("AUTH_USERNAME"))
	log.Printf("DEBUG: AUTH_PASSWORD direct read: '%s'", os.Getenv("AUTH_PASSWORD"))

	// Try to read .env file directly
	if data, err := os.ReadFile(".env"); err == nil {
		log.Printf("DEBUG: .env file contents:\n%s", string(data))
	} else {
		log.Printf("DEBUG: Error reading .env file: %v", err)
	}

	Auth = AuthConfig{
		Username: GetEnvOrDefault("AUTH_USERNAME", "admin"),
		Password: GetEnvOrDefault("AUTH_PASSWORD", "change_me_in_production"),
	}

	// Debug: Print final configuration
	log.Printf("DEBUG: Final auth configuration:")
	log.Printf("Username=%s", Auth.Username)
	log.Printf("Password=%s", Auth.Password)
}

// ValidateCredentials checks if the provided credentials match the configured ones
func ValidateCredentials(username, password string) bool {
	log.Printf("Validating credentials for user: %s", username)
	log.Printf("Using configured username: %s", Auth.Username)
	return subtle.ConstantTimeCompare([]byte(username), []byte(Auth.Username)) == 1 &&
		subtle.ConstantTimeCompare([]byte(password), []byte(Auth.Password)) == 1
}

// ValidateBasicAuth validates the Basic Authentication credentials from the request
func ValidateBasicAuth(r *http.Request) bool {
	username, password, ok := r.BasicAuth()
	if !ok {
		return false
	}
	return ValidateCredentials(username, password)
}

// GetEnvOrDefault returns environment variable value or default if not set
func GetEnvOrDefault(key, defaultValue string) string {
	value := os.Getenv(key)
	log.Printf("DEBUG: GetEnvOrDefault: key='%s', value='%s', default='%s'", key, value, defaultValue)
	if value != "" {
		return value
	}
	return defaultValue
} 