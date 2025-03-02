package models

import (
	"time"
)

// User represents a user in the system
type User struct {
	ID        int64     `json:"id" db:"id"`
	Email     string    `json:"email" db:"email"`
	Password  string    `json:"-" db:"password_hash"`
	Name      string    `json:"name" db:"name"`
	CreatedAt time.Time `json:"created_at" db:"created_at"`
	UpdatedAt time.Time `json:"updated_at" db:"updated_at"`
}

// UserProfile represents public user information
type UserProfile struct {
	ID    int64  `json:"id"`
	Email string `json:"email"`
	Name  string `json:"name"`
}

// LoginRequest represents the login credentials
type LoginRequest struct {
	Email    string `json:"email"`
	Password string `json:"password"`
}

// RegisterRequest represents the registration data
type RegisterRequest struct {
	Email    string `json:"email"`
	Password string `json:"password"`
	Name     string `json:"name"`
}

// TokenResponse represents the authentication token response
type TokenResponse struct {
	AccessToken  string `json:"access_token"`
	TokenType    string `json:"token_type"`
	ExpiresIn    int    `json:"expires_in"`
	RefreshToken string `json:"refresh_token,omitempty"`
}

// ToProfile converts a User to a UserProfile
func (u *User) ToProfile() *UserProfile {
	return &UserProfile{
		ID:    u.ID,
		Email: u.Email,
		Name:  u.Name,
	}
}

// Validate performs basic validation on user registration data
func (r *RegisterRequest) Validate() error {
	// TODO: Implement validation logic
	// - Check email format
	// - Verify password strength
	// - Validate name length
	return nil
}

// Validate performs basic validation on login data
func (r *LoginRequest) Validate() error {
	// TODO: Implement validation logic
	// - Check email format
	// - Verify password is not empty
	return nil
} 