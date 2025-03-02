package postgres

import (
	"context"
	"database/sql"
	"fmt"
	"time"

	_ "github.com/lib/pq"
	"universa.web/internal/models"
)

// Config holds database configuration
type Config struct {
	Host     string
	Port     string
	User     string
	Password string
	DBName   string
	SSLMode  string
}

// Store represents a PostgreSQL storage implementation
type Store struct {
	db *sql.DB
}

// NewStore creates a new PostgreSQL store
func NewStore(config Config) (*Store, error) {
	connStr := fmt.Sprintf(
		"host=%s port=%s user=%s password=%s dbname=%s sslmode=%s",
		config.Host, config.Port, config.User, config.Password, config.DBName, config.SSLMode,
	)

	db, err := sql.Open("postgres", connStr)
	if err != nil {
		return nil, fmt.Errorf("error opening database: %w", err)
	}

	// Test the connection
	if err := db.Ping(); err != nil {
		return nil, fmt.Errorf("error connecting to the database: %w", err)
	}

	return &Store{db: db}, nil
}

// Close closes the database connection
func (s *Store) Close() error {
	return s.db.Close()
}

// CreateUser creates a new user in the database
func (s *Store) CreateUser(ctx context.Context, user *models.User) error {
	query := `
		INSERT INTO users (email, password_hash, name, created_at, updated_at)
		VALUES ($1, $2, $3, $4, $5)
		RETURNING id`

	now := time.Now()
	user.CreatedAt = now
	user.UpdatedAt = now

	return s.db.QueryRowContext(ctx, query,
		user.Email,
		user.Password,
		user.Name,
		user.CreatedAt,
		user.UpdatedAt,
	).Scan(&user.ID)
}

// GetUserByEmail retrieves a user by email
func (s *Store) GetUserByEmail(ctx context.Context, email string) (*models.User, error) {
	query := `
		SELECT id, email, password_hash, name, created_at, updated_at
		FROM users
		WHERE email = $1`

	user := &models.User{}
	err := s.db.QueryRowContext(ctx, query, email).Scan(
		&user.ID,
		&user.Email,
		&user.Password,
		&user.Name,
		&user.CreatedAt,
		&user.UpdatedAt,
	)

	if err == sql.ErrNoRows {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}

	return user, nil
}

// UpdateUser updates user information
func (s *Store) UpdateUser(ctx context.Context, user *models.User) error {
	query := `
		UPDATE users
		SET email = $1, name = $2, updated_at = $3
		WHERE id = $4`

	user.UpdatedAt = time.Now()

	result, err := s.db.ExecContext(ctx, query,
		user.Email,
		user.Name,
		user.UpdatedAt,
		user.ID,
	)
	if err != nil {
		return err
	}

	rows, err := result.RowsAffected()
	if err != nil {
		return err
	}

	if rows == 0 {
		return fmt.Errorf("user not found")
	}

	return nil
}

// DeleteUser deletes a user from the database
func (s *Store) DeleteUser(ctx context.Context, id int64) error {
	query := `DELETE FROM users WHERE id = $1`

	result, err := s.db.ExecContext(ctx, query, id)
	if err != nil {
		return err
	}

	rows, err := result.RowsAffected()
	if err != nil {
		return err
	}

	if rows == 0 {
		return fmt.Errorf("user not found")
	}

	return nil
}

// InitSchema initializes the database schema
func (s *Store) InitSchema(ctx context.Context) error {
	queries := []string{
		`CREATE TABLE IF NOT EXISTS users (
			id SERIAL PRIMARY KEY,
			email VARCHAR(255) UNIQUE NOT NULL,
			password_hash VARCHAR(255) NOT NULL,
			name VARCHAR(255) NOT NULL,
			created_at TIMESTAMP NOT NULL,
			updated_at TIMESTAMP NOT NULL
		)`,
		// Add more table creation queries as needed
	}

	for _, query := range queries {
		_, err := s.db.ExecContext(ctx, query)
		if err != nil {
			return fmt.Errorf("error creating schema: %w", err)
		}
	}

	return nil
} 