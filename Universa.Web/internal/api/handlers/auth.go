package handlers

import (
	"encoding/json"
	"net/http"
	"time"
	"universa.web/internal/auth/jwt"
	"universa.web/internal/models"
)

// AuthHandler handles authentication-related requests
type AuthHandler struct {
	jwtService *jwt.Service
	// TODO: Add user service/repository
}

// NewAuthHandler creates a new authentication handler
func NewAuthHandler(jwtService *jwt.Service) *AuthHandler {
	return &AuthHandler{
		jwtService: jwtService,
	}
}

// Register handles user registration
func (h *AuthHandler) Register(w http.ResponseWriter, r *http.Request) {
	var req models.RegisterRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		BadRequest(w, "Invalid request payload")
		return
	}

	if err := req.Validate(); err != nil {
		ValidationError(w, err.Error())
		return
	}

	// TODO: Check if user exists
	// TODO: Hash password
	// TODO: Create user in database

	RespondWithJSON(w, http.StatusCreated, map[string]string{
		"message": "User registered successfully",
	})
}

// Login handles user login
func (h *AuthHandler) Login(w http.ResponseWriter, r *http.Request) {
	var req models.LoginRequest
	if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
		BadRequest(w, "Invalid request payload")
		return
	}

	if err := req.Validate(); err != nil {
		ValidationError(w, err.Error())
		return
	}

	// TODO: Verify user credentials
	// For now, return a mock response
	mockUserID := int64(1)
	token, err := h.jwtService.GenerateToken(mockUserID, req.Email)
	if err != nil {
		InternalServerError(w, "Error generating token")
		return
	}

	response := models.TokenResponse{
		AccessToken: token,
		TokenType:   "Bearer",
		ExpiresIn:   int(time.Hour.Seconds()),
	}

	RespondWithJSON(w, http.StatusOK, response)
}

// RegisterRoutes registers the authentication routes
func (h *AuthHandler) RegisterRoutes(mux *http.ServeMux) {
	mux.HandleFunc("/api/auth/register", h.Register)
	mux.HandleFunc("/api/auth/login", h.Login)
} 