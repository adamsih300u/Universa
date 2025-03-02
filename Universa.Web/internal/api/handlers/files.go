package handlers

import (
	"io"
	"net/http"
	"path"
	"strings"

	"universa.web/internal/api/middleware"
	"universa.web/internal/storage/files"
)

// FileHandler handles file-related requests
type FileHandler struct {
	storage *files.Storage
}

// NewFileHandler creates a new file handler
func NewFileHandler(storage *files.Storage) *FileHandler {
	return &FileHandler{
		storage: storage,
	}
}

// HandleUpload handles file upload requests
func (h *FileHandler) HandleUpload(w http.ResponseWriter, r *http.Request) {
	// Get user ID from context
	userID, ok := middleware.GetUserID(r.Context())
	if !ok {
		Unauthorized(w, "User not authenticated")
		return
	}

	// Get file path from URL
	filePath := strings.TrimPrefix(r.URL.Path, "/api/files/")
	if filePath == "" {
		BadRequest(w, "File path is required")
		return
	}

	// Read file content
	content, err := io.ReadAll(r.Body)
	if err != nil {
		InternalServerError(w, "Failed to read request body")
		return
	}

	// Store file
	info, err := h.storage.Store(userID, filePath, content)
	if err != nil {
		InternalServerError(w, "Failed to store file")
		return
	}

	RespondWithJSON(w, http.StatusOK, info)
}

// HandleDownload handles file download requests
func (h *FileHandler) HandleDownload(w http.ResponseWriter, r *http.Request) {
	userID, ok := middleware.GetUserID(r.Context())
	if !ok {
		Unauthorized(w, "User not authenticated")
		return
	}

	filePath := strings.TrimPrefix(r.URL.Path, "/api/files/")
	if filePath == "" {
		BadRequest(w, "File path is required")
		return
	}

	content, err := h.storage.Get(userID, filePath)
	if err != nil {
		NotFound(w, "File not found")
		return
	}

	// Set content type based on file extension
	contentType := "application/octet-stream"
	ext := path.Ext(filePath)
	if ext != "" {
		switch strings.ToLower(ext) {
		case ".txt", ".md":
			contentType = "text/plain"
		case ".json":
			contentType = "application/json"
		case ".html":
			contentType = "text/html"
		}
	}

	w.Header().Set("Content-Type", contentType)
	w.Write(content)
}

// HandleDelete handles file deletion requests
func (h *FileHandler) HandleDelete(w http.ResponseWriter, r *http.Request) {
	userID, ok := middleware.GetUserID(r.Context())
	if !ok {
		Unauthorized(w, "User not authenticated")
		return
	}

	filePath := strings.TrimPrefix(r.URL.Path, "/api/files/")
	if filePath == "" {
		BadRequest(w, "File path is required")
		return
	}

	if err := h.storage.Delete(userID, filePath); err != nil {
		NotFound(w, "File not found")
		return
	}

	RespondWithJSON(w, http.StatusOK, map[string]string{
		"message": "File deleted successfully",
	})
}

// HandleInfo handles file info requests
func (h *FileHandler) HandleInfo(w http.ResponseWriter, r *http.Request) {
	userID, ok := middleware.GetUserID(r.Context())
	if !ok {
		Unauthorized(w, "User not authenticated")
		return
	}

	filePath := strings.TrimPrefix(r.URL.Path, "/api/files/")
	filePath = strings.TrimSuffix(filePath, "/info")
	if filePath == "" {
		BadRequest(w, "File path is required")
		return
	}

	info, err := h.storage.GetInfo(userID, filePath)
	if err != nil {
		NotFound(w, "File not found")
		return
	}

	RespondWithJSON(w, http.StatusOK, info)
}

// HandleList handles directory listing requests
func (h *FileHandler) HandleList(w http.ResponseWriter, r *http.Request) {
	userID, ok := middleware.GetUserID(r.Context())
	if !ok {
		Unauthorized(w, "User not authenticated")
		return
	}

	dirPath := strings.TrimPrefix(r.URL.Path, "/api/files")
	if dirPath == "" {
		dirPath = "/"
	}

	files, err := h.storage.List(userID, dirPath)
	if err != nil {
		NotFound(w, "Directory not found")
		return
	}

	RespondWithJSON(w, http.StatusOK, files)
}

// RegisterRoutes registers the file handler routes
func (h *FileHandler) RegisterRoutes(mux *http.ServeMux) {
	mux.HandleFunc("/api/files/", func(w http.ResponseWriter, r *http.Request) {
		switch r.Method {
		case http.MethodGet:
			if strings.HasSuffix(r.URL.Path, "/info") {
				h.HandleInfo(w, r)
			} else {
				h.HandleDownload(w, r)
			}
		case http.MethodPut:
			h.HandleUpload(w, r)
		case http.MethodDelete:
			h.HandleDelete(w, r)
		default:
			w.WriteHeader(http.StatusMethodNotAllowed)
		}
	})
} 