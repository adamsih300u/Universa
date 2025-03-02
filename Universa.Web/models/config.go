package models

type Config struct {
    AI      AIConfig      `json:"ai"`
    Theme   ThemeConfig   `json:"theme"`
    Weather WeatherConfig `json:"weather"`
}

type AIConfig struct {
    OpenAI struct {
        Enabled bool   `json:"enabled"`
        APIKey  string `json:"apiKey"`
    } `json:"openai"`
    Anthropic struct {
        Enabled bool   `json:"enabled"`
        APIKey  string `json:"apiKey"`
    } `json:"anthropic"`
    XAI struct {
        Enabled bool   `json:"enabled"`
        APIKey  string `json:"apiKey"`
    } `json:"xai"`
    Ollama struct {
        Enabled bool   `json:"enabled"`
        URL     string `json:"url"`
        Model   string `json:"model"`
    } `json:"ollama"`
    DefaultProvider string `json:"defaultProvider"`
    EnableAIChat    bool   `json:"enableAIChat"`
    LastUsedModel   string `json:"lastUsedModel"`
}

type ThemeConfig struct {
    Current string `json:"current"`
    Colors  struct {
        DarkMode struct {
            Text string `json:"text"`
        } `json:"darkMode"`
        LightMode struct {
            Text string `json:"text"`
        } `json:"lightMode"`
    } `json:"colors"`
}

type WeatherConfig struct {
    Enabled        bool   `json:"enabled"`
    ZipCode        string `json:"zipCode"`
    APIKey         string `json:"apiKey"`
    EnableMoonPhase bool  `json:"enableMoonPhase"`
}

type UniversaConfig struct {
    Services ServiceConfig `json:"services"`
    UI       UIConfig     `json:"ui"`
}

type ServiceConfig struct {
    RSS      RSSConfig      `json:"rss"`
    Subsonic SubsonicConfig `json:"subsonic"`
    AI       AIConfig       `json:"ai"`
}

type RSSConfig struct {
    Feeds     []FeedConfig `json:"feeds"`
    UpdateInterval int    `json:"updateInterval"`
}

type SubsonicConfig struct {
    Server   string `json:"server"`
    Username string `json:"username"`
    Password string `json:"password"`
    Version  string `json:"version"`
}

type FeedConfig struct {
    URL      string `json:"url"`
    Name     string `json:"name"`
    Category string `json:"category"`
}

type UIConfig struct {
    Theme            string `json:"theme"`
    SidebarPosition  string `json:"sidebarPosition"`
    DefaultView     string `json:"defaultView"`
} 