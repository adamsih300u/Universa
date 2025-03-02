package protocol

import (
	"time"
)

// FileOperation represents the type of file operation
type FileOperation string

const (
	// File operations
	FileCreate FileOperation = "create"
	FileUpdate FileOperation = "update"
	FileDelete FileOperation = "delete"
	FileRename FileOperation = "rename"
)

// SyncMessage represents a synchronization message
type SyncMessage struct {
	Operation FileOperation `json:"operation"`
	Path      string       `json:"path"`
	NewPath   string       `json:"new_path,omitempty"` // For rename operations
	Content   string       `json:"content,omitempty"`  // Base64 encoded for binary files
	Hash      string       `json:"hash,omitempty"`     // Content hash for verification
	Timestamp time.Time    `json:"timestamp"`
}

// SyncRequest represents a request to sync files
type SyncRequest struct {
	Files []SyncMessage `json:"files"`
}

// SyncResponse represents a response to a sync request
type SyncResponse struct {
	Success    bool           `json:"success"`
	Error      string         `json:"error,omitempty"`
	Conflicts  []SyncMessage  `json:"conflicts,omitempty"`
	Timestamp  time.Time      `json:"timestamp"`
}

// FileMetadata represents metadata about a file
type FileMetadata struct {
	Path         string    `json:"path"`
	Size         int64     `json:"size"`
	Hash         string    `json:"hash"`
	LastModified time.Time `json:"last_modified"`
	IsDirectory  bool      `json:"is_directory"`
}

// SyncState represents the current synchronization state
type SyncState struct {
	LastSync  time.Time               `json:"last_sync"`
	Files     map[string]FileMetadata `json:"files"`
	Conflicts []string                `json:"conflicts,omitempty"`
}

// DiffRequest represents a request to get file differences
type DiffRequest struct {
	Path      string    `json:"path"`
	Timestamp time.Time `json:"timestamp"`
	Hash      string    `json:"hash"`
}

// DiffResponse represents the response containing file differences
type DiffResponse struct {
	HasChanges bool   `json:"has_changes"`
	Diff       string `json:"diff,omitempty"`      // Base64 encoded diff
	NewHash    string `json:"new_hash,omitempty"`
	Error      string `json:"error,omitempty"`
}

// ValidateHash checks if the provided hash matches the content
func ValidateHash(content, hash string) bool {
	// TODO: Implement hash validation
	return true
}

// GenerateHash generates a hash for the given content
func GenerateHash(content string) string {
	// TODO: Implement hash generation
	return ""
}

// ApplyDiff applies a diff to the content
func ApplyDiff(content, diff string) (string, error) {
	// TODO: Implement diff application
	return content, nil
}

// GenerateDiff generates a diff between two versions of content
func GenerateDiff(oldContent, newContent string) (string, error) {
	// TODO: Implement diff generation
	return "", nil
} 