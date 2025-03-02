package files

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"time"
)

// Storage represents a file storage implementation
type Storage struct {
	basePath string
}

// FileInfo represents information about a stored file
type FileInfo struct {
	Path         string
	Size         int64
	Hash         string
	LastModified time.Time
	IsDirectory  bool
}

// NewStorage creates a new file storage instance
func NewStorage(basePath string) (*Storage, error) {
	// Create base directory if it doesn't exist
	if err := os.MkdirAll(basePath, 0755); err != nil {
		return nil, fmt.Errorf("failed to create base directory: %w", err)
	}

	return &Storage{
		basePath: basePath,
	}, nil
}

// Store stores a file in the storage
func (s *Storage) Store(userID int64, path string, content []byte) (*FileInfo, error) {
	fullPath := s.getFullPath(userID, path)

	// Create parent directories if they don't exist
	if err := os.MkdirAll(filepath.Dir(fullPath), 0755); err != nil {
		return nil, fmt.Errorf("failed to create directories: %w", err)
	}

	// Write file
	if err := os.WriteFile(fullPath, content, 0644); err != nil {
		return nil, fmt.Errorf("failed to write file: %w", err)
	}

	// Get file info
	info, err := s.GetInfo(userID, path)
	if err != nil {
		return nil, fmt.Errorf("failed to get file info: %w", err)
	}

	return info, nil
}

// Get retrieves a file from storage
func (s *Storage) Get(userID int64, path string) ([]byte, error) {
	fullPath := s.getFullPath(userID, path)

	content, err := os.ReadFile(fullPath)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, fmt.Errorf("file not found: %s", path)
		}
		return nil, fmt.Errorf("failed to read file: %w", err)
	}

	return content, nil
}

// Delete removes a file from storage
func (s *Storage) Delete(userID int64, path string) error {
	fullPath := s.getFullPath(userID, path)

	if err := os.Remove(fullPath); err != nil {
		if os.IsNotExist(err) {
			return fmt.Errorf("file not found: %s", path)
		}
		return fmt.Errorf("failed to delete file: %w", err)
	}

	return nil
}

// List lists files in a directory
func (s *Storage) List(userID int64, path string) ([]FileInfo, error) {
	fullPath := s.getFullPath(userID, path)

	entries, err := os.ReadDir(fullPath)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, fmt.Errorf("directory not found: %s", path)
		}
		return nil, fmt.Errorf("failed to read directory: %w", err)
	}

	var files []FileInfo
	for _, entry := range entries {
		relativePath := filepath.Join(path, entry.Name())
		fileInfo, err := s.GetInfo(userID, relativePath)
		if err != nil {
			continue
		}

		files = append(files, *fileInfo)
	}

	return files, nil
}

// GetInfo gets information about a file
func (s *Storage) GetInfo(userID int64, path string) (*FileInfo, error) {
	fullPath := s.getFullPath(userID, path)

	info, err := os.Stat(fullPath)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, fmt.Errorf("file not found: %s", path)
		}
		return nil, fmt.Errorf("failed to get file info: %w", err)
	}

	fileInfo := &FileInfo{
		Path:         path,
		Size:         info.Size(),
		LastModified: info.ModTime(),
		IsDirectory:  info.IsDir(),
	}

	if !info.IsDir() {
		hash, err := s.calculateHash(fullPath)
		if err != nil {
			return nil, fmt.Errorf("failed to calculate hash: %w", err)
		}
		fileInfo.Hash = hash
	}

	return fileInfo, nil
}

// calculateHash calculates the SHA-256 hash of a file
func (s *Storage) calculateHash(path string) (string, error) {
	file, err := os.Open(path)
	if err != nil {
		return "", err
	}
	defer file.Close()

	hash := sha256.New()
	if _, err := io.Copy(hash, file); err != nil {
		return "", err
	}

	return hex.EncodeToString(hash.Sum(nil)), nil
}

// getFullPath returns the full path for a user's file
func (s *Storage) getFullPath(userID int64, path string) string {
	return filepath.Join(s.basePath, fmt.Sprintf("%d", userID), path)
}

// Move moves or renames a file
func (s *Storage) Move(userID int64, oldPath, newPath string) error {
	oldFullPath := s.getFullPath(userID, oldPath)
	newFullPath := s.getFullPath(userID, newPath)

	// Create parent directories if they don't exist
	if err := os.MkdirAll(filepath.Dir(newFullPath), 0755); err != nil {
		return fmt.Errorf("failed to create directories: %w", err)
	}

	if err := os.Rename(oldFullPath, newFullPath); err != nil {
		return fmt.Errorf("failed to move file: %w", err)
	}

	return nil
} 