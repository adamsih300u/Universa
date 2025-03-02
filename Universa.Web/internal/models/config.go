package models

import (
	"encoding/json"
	"os"
)

// Config holds all configuration for the application
type Config struct {
	Server   ServerConfig   `json:"server"`
	Database DatabaseConfig `json:"database"`
}

// ServerConfig holds HTTP server configuration
type ServerConfig struct {
	Host            string `json:"host"`
	Port            string `json:"port"`
	ReadTimeoutSec  int    `json:"read_timeout_sec"`
	WriteTimeoutSec int    `json:"write_timeout_sec"`
	IdleTimeoutSec  int    `json:"idle_timeout_sec"`
}

// DatabaseConfig holds database configuration
type DatabaseConfig struct {
	Host     string `json:"host"`
	Port     string `json:"port"`
	User     string `json:"user"`
	Password string `json:"password"`
	DBName   string `json:"dbname"`
	SSLMode  string `json:"sslmode"`
}

// LoadConfig loads configuration from a JSON file
func LoadConfig(path string) (*Config, error) {
	file, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer file.Close()

	config := &Config{}
	decoder := json.NewDecoder(file)
	if err := decoder.Decode(config); err != nil {
		return nil, err
	}

	return config, nil
}

// DefaultConfig returns a default configuration
func DefaultConfig() *Config {
	return &Config{
		Server: ServerConfig{
			Host:            "localhost",
			Port:            "8080",
			ReadTimeoutSec:  15,
			WriteTimeoutSec: 15,
			IdleTimeoutSec:  60,
		},
		Database: DatabaseConfig{
			Host:     "localhost",
			Port:     "5432",
			User:     "postgres",
			Password: "",
			DBName:   "universa",
			SSLMode:  "disable",
		},
	}
} 