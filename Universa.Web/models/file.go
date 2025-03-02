package models

import (
    "time"
    "path/filepath"
    "crypto/sha256"
    "io"
    "os"
    "fmt"
    "log"
)

type FileMetadata struct {
    Name    string    `json:"name"`
    Path    string    `json:"path"`
    Size    int64     `json:"size"`
    IsDir   bool      `json:"isDir"`
    ModTime time.Time `json:"modTime"`
    Hash    string    `json:"hash"`
}

func NewFileMetadata(basePath, relativePath string) (*FileMetadata, error) {
    log.Printf("Creating metadata for file: %s in base path: %s", relativePath, basePath)
    fullPath := filepath.Join(basePath, relativePath)
    
    info, err := os.Stat(fullPath)
    if err != nil {
        log.Printf("Error getting file info: %v", err)
        return nil, err
    }
    
    // Get file hash if it's not a directory
    var hash string
    if !info.IsDir() {
        hash, err = calculateFileHash(fullPath)
        if err != nil {
            log.Printf("Error calculating file hash: %v", err)
            return nil, err
        }
    }
    
    metadata := &FileMetadata{
        Name:    filepath.Base(relativePath),
        Path:    relativePath,
        Size:    info.Size(),
        IsDir:   info.IsDir(),
        ModTime: info.ModTime(),
        Hash:    hash,
    }
    
    log.Printf("Created metadata: %+v", metadata)
    return metadata, nil
}

func calculateFileHash(path string) (string, error) {
    f, err := os.Open(path)
    if err != nil {
        return "", err
    }
    defer f.Close()

    h := sha256.New()
    if _, err := io.Copy(h, f); err != nil {
        return "", err
    }

    return fmt.Sprintf("%x", h.Sum(nil)), nil
} 