package middleware

import (
	"context"
	"net/http"
	"universa.web/internal/auth/jwt"
)

type contextKey string

const (
	UserIDKey contextKey = "user_id"
	EmailKey  contextKey = "email"
)

// AuthMiddleware creates a new authentication middleware
func AuthMiddleware(jwtService *jwt.Service) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			// Get token from header
			authHeader := r.Header.Get("Authorization")
			if authHeader == "" {
				http.Error(w, "Authorization header required", http.StatusUnauthorized)
				return
			}

			// Extract token
			tokenString := jwt.ExtractTokenFromHeader(authHeader)
			if tokenString == "" {
				http.Error(w, "Invalid token format", http.StatusUnauthorized)
				return
			}

			// Validate token
			claims, err := jwtService.ValidateToken(tokenString)
			if err != nil {
				http.Error(w, "Invalid token", http.StatusUnauthorized)
				return
			}

			// Add claims to context
			ctx := context.WithValue(r.Context(), UserIDKey, claims.UserID)
			ctx = context.WithValue(ctx, EmailKey, claims.Email)

			// Call next handler with updated context
			next.ServeHTTP(w, r.WithContext(ctx))
		})
	}
}

// GetUserID retrieves the user ID from the request context
func GetUserID(ctx context.Context) (int64, bool) {
	id, ok := ctx.Value(UserIDKey).(int64)
	return id, ok
}

// GetUserEmail retrieves the user email from the request context
func GetUserEmail(ctx context.Context) (string, bool) {
	email, ok := ctx.Value(EmailKey).(string)
	return email, ok
} 