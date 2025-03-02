package main

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"universa.web/internal/api/handlers"
	"universa.web/internal/api/middleware"
	"universa.web/internal/auth/jwt"
	"universa.web/internal/models"
	"universa.web/internal/storage/files"
	"universa.web/internal/storage/postgres"
	"universa.web/internal/sync/websocket"
)

func main() {
	// Load configuration
	config := loadConfig()

	// Initialize storage
	db, err := initializeStorage(config.Database)
	if err != nil {
		log.Fatalf("Failed to initialize storage: %v", err)
	}
	defer db.Close()

	// Initialize file storage
	fileStorage, err := files.NewStorage(os.Getenv("FILE_STORAGE_PATH"))
	if err != nil {
		log.Fatalf("Failed to initialize file storage: %v", err)
	}

	// Initialize JWT service
	jwtService := jwt.NewService(jwt.Config{
		SecretKey:     os.Getenv("JWT_SECRET"),
		TokenDuration: 24 * time.Hour,
	})

	// Initialize WebSocket handler
	wsHandler := websocket.NewHandler()

	// Create a new server mux
	mux := http.NewServeMux()

	// Add middleware
	handler := middleware.LoggingMiddleware(mux)
	handler = middleware.RequestIDMiddleware(handler)

	// Create auth middleware
	authMiddleware := middleware.AuthMiddleware(jwtService)

	// Initialize handlers
	authHandler := handlers.NewAuthHandler(jwtService)
	fileHandler := handlers.NewFileHandler(fileStorage)

	// Register routes
	authHandler.RegisterRoutes(mux)

	// Protected file routes
	protectedFiles := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path == "/api/files" || r.URL.Path == "/api/files/" {
			switch r.Method {
			case http.MethodGet:
				fileHandler.HandleList(w, r)
			default:
				w.WriteHeader(http.StatusMethodNotAllowed)
			}
			return
		}
		fileHandler.RegisterRoutes(mux)
	})
	mux.Handle("/api/files/", authMiddleware(protectedFiles))

	// Add basic health check endpoint
	mux.HandleFunc("/health", func(w http.ResponseWriter, r *http.Request) {
		w.WriteHeader(http.StatusOK)
		w.Write([]byte("OK"))
	})

	// Add WebSocket endpoint with authentication
	wsEndpoint := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		wsHandler.HandleWebSocket(w, r, 0) // UserID will be set by auth middleware
	})
	mux.Handle("/ws", authMiddleware(wsEndpoint))

	// Configure the server
	server := &http.Server{
		Addr:         fmt.Sprintf("%s:%s", config.Server.Host, config.Server.Port),
		Handler:      handler,
		ReadTimeout:  time.Duration(config.Server.ReadTimeoutSec) * time.Second,
		WriteTimeout: time.Duration(config.Server.WriteTimeoutSec) * time.Second,
		IdleTimeout:  time.Duration(config.Server.IdleTimeoutSec) * time.Second,
	}

	// Start the server in a goroutine
	go func() {
		log.Printf("Starting server on %s\n", server.Addr)
		if err := server.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("Server error: %v\n", err)
		}
	}()

	// Set up graceful shutdown
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)

	// Wait for interrupt signal
	<-quit
	log.Println("Server is shutting down...")

	// Create shutdown context with 30 second timeout
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	// Attempt graceful shutdown
	if err := server.Shutdown(ctx); err != nil {
		log.Fatalf("Server forced to shutdown: %v\n", err)
	}

	log.Println("Server exited properly")
}

func loadConfig() *models.Config {
	// Try to load config from file
	config, err := models.LoadConfig("config.json")
	if err != nil {
		// Use default config if file not found
		config = models.DefaultConfig()
	}

	// Override with environment variables if present
	if port := os.Getenv("PORT"); port != "" {
		config.Server.Port = port
	}
	if dbHost := os.Getenv("DB_HOST"); dbHost != "" {
		config.Database.Host = dbHost
	}
	if dbPort := os.Getenv("DB_PORT"); dbPort != "" {
		config.Database.Port = dbPort
	}
	if dbUser := os.Getenv("DB_USER"); dbUser != "" {
		config.Database.User = dbUser
	}
	if dbPass := os.Getenv("DB_PASSWORD"); dbPass != "" {
		config.Database.Password = dbPass
	}
	if dbName := os.Getenv("DB_NAME"); dbName != "" {
		config.Database.DBName = dbName
	}
	if dbSSL := os.Getenv("DB_SSLMODE"); dbSSL != "" {
		config.Database.SSLMode = dbSSL
	}

	return config
}

func initializeStorage(config models.DatabaseConfig) (*postgres.Store, error) {
	store, err := postgres.NewStore(postgres.Config{
		Host:     config.Host,
		Port:     config.Port,
		User:     config.User,
		Password: config.Password,
		DBName:   config.DBName,
		SSLMode:  config.SSLMode,
	})
	if err != nil {
		return nil, fmt.Errorf("failed to create store: %w", err)
	}

	// Initialize schema
	if err := store.InitSchema(context.Background()); err != nil {
		return nil, fmt.Errorf("failed to initialize schema: %w", err)
	}

	return store, nil
} 