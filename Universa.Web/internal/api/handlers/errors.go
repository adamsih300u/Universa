package handlers

import (
	"encoding/json"
	"net/http"
)

// ErrorResponse represents an error response
type ErrorResponse struct {
	Error   string `json:"error"`
	Message string `json:"message,omitempty"`
	Code    int    `json:"code"`
}

// Common error types
var (
	ErrInvalidRequest    = "invalid_request"
	ErrUnauthorized      = "unauthorized"
	ErrForbidden         = "forbidden"
	ErrNotFound          = "not_found"
	ErrInternalServer    = "internal_server_error"
	ErrValidation        = "validation_error"
	ErrDatabaseOperation = "database_error"
)

// RespondWithError sends an error response
func RespondWithError(w http.ResponseWriter, code int, errorType, message string) {
	RespondWithJSON(w, code, ErrorResponse{
		Error:   errorType,
		Message: message,
		Code:    code,
	})
}

// RespondWithJSON sends a JSON response
func RespondWithJSON(w http.ResponseWriter, code int, payload interface{}) {
	response, err := json.Marshal(payload)
	if err != nil {
		w.WriteHeader(http.StatusInternalServerError)
		return
	}

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(code)
	w.Write(response)
}

// BadRequest sends a 400 Bad Request response
func BadRequest(w http.ResponseWriter, message string) {
	RespondWithError(w, http.StatusBadRequest, ErrInvalidRequest, message)
}

// Unauthorized sends a 401 Unauthorized response
func Unauthorized(w http.ResponseWriter, message string) {
	RespondWithError(w, http.StatusUnauthorized, ErrUnauthorized, message)
}

// Forbidden sends a 403 Forbidden response
func Forbidden(w http.ResponseWriter, message string) {
	RespondWithError(w, http.StatusForbidden, ErrForbidden, message)
}

// NotFound sends a 404 Not Found response
func NotFound(w http.ResponseWriter, message string) {
	RespondWithError(w, http.StatusNotFound, ErrNotFound, message)
}

// InternalServerError sends a 500 Internal Server Error response
func InternalServerError(w http.ResponseWriter, message string) {
	RespondWithError(w, http.StatusInternalServerError, ErrInternalServer, message)
}

// ValidationError sends a 422 Unprocessable Entity response
func ValidationError(w http.ResponseWriter, message string) {
	RespondWithError(w, http.StatusUnprocessableEntity, ErrValidation, message)
} 