package services

import (
    "encoding/json"
    "log"
    "os"
    "path/filepath"
    "sync"
    "universa.web/models"
)

type ConfigService struct {
    basePath string
    mutex      sync.RWMutex
}

func NewConfigService(basePath string) *ConfigService {
    return &ConfigService{
        basePath: basePath,
    }
}

func (s *ConfigService) GetUserConfigPath(username string) (string, error) {
    // For now, we'll use a simple path structure: basePath/username/.universa/config.json
    configPath := filepath.Join(s.basePath, username, ".universa", "config.json")
    
    // Ensure the .universa directory exists
    configDir := filepath.Dir(configPath)
    log.Printf("Creating config directory: %s", configDir)
    if err := os.MkdirAll(configDir, 0755); err != nil {
        log.Printf("ERROR: Failed to create config directory: %v", err)
        return "", err
    }
    
    // If config doesn't exist, create default
    if _, err := os.Stat(configPath); os.IsNotExist(err) {
        log.Printf("Creating default config for user: %s", username)
        defaultConfig := models.Config{
            AI: models.AIConfig{
                OpenAI: struct {
                    Enabled bool   `json:"enabled"`
                    APIKey  string `json:"apiKey"`
                }{
                    Enabled: false,
                    APIKey:  "",
                },
                Anthropic: struct {
                    Enabled bool   `json:"enabled"`
                    APIKey  string `json:"apiKey"`
                }{
                    Enabled: false,
                    APIKey:  "",
                },
                DefaultProvider: "OpenAI",
                EnableAIChat:   true,
            },
            Theme: models.ThemeConfig{
                Current: "Dark",
            },
            Weather: models.WeatherConfig{
                Enabled:        false,
                EnableMoonPhase: false,
            },
        }
        
        if err := s.SaveConfig(username, &defaultConfig); err != nil {
            log.Printf("ERROR: Failed to save default config: %v", err)
            return "", err
        }
        log.Printf("Successfully created default config")
    }
    
    return configPath, nil
}

func (s *ConfigService) LoadConfig(username string) (*models.Config, error) {
    log.Printf("Loading config for user: %s", username)
    configPath, err := s.GetUserConfigPath(username)
    if err != nil {
        log.Printf("Error getting config path: %v", err)
        return nil, err
    }
    
    log.Printf("Reading config from: %s", configPath)
    data, err := os.ReadFile(configPath)
    if err != nil {
        log.Printf("Error reading config file: %v", err)
        return nil, err
    }
    
    var config models.Config
    if err := json.Unmarshal(data, &config); err != nil {
        log.Printf("Error parsing config JSON: %v", err)
        return nil, err
    }
    
    log.Printf("Successfully loaded config for user: %s", username)
    return &config, nil
}

func (s *ConfigService) SaveConfig(username string, config *models.Config) error {
    configPath, err := s.GetUserConfigPath(username)
    if err != nil {
        return err
    }
    
    data, err := json.MarshalIndent(config, "", "    ")
    if err != nil {
        return err
    }
    
    return os.WriteFile(configPath, data, 0644)
} 