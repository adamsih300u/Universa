package main

import (
	"github.com/gorilla/mux"
	"net/http"
	"time"
	"log"
	"universa.web/config"
	"universa.web/middleware"
	"encoding/json"
	"path/filepath"
	"os"
	"universa.web/models"
	"github.com/gorilla/websocket"
	"io"
	"fmt"
	"strings"
	"sync"
	"universa.web/services"
	"universa.web/session"
	"sort"
	"context"
)

// Add this structure to manage WebSocket connections
type FileChangeNotifier struct {
	clients    map[*websocket.Conn]chan FileChange
	register   chan *websocket.Conn
	unregister chan *websocket.Conn
	broadcast  chan FileChange
	mutex      sync.RWMutex
}

type FileChange struct {
	Type         string           `json:"type"`    // "create", "update", "delete"
	FileMetadata models.FileMetadata `json:"file"`
}

var notifier = FileChangeNotifier{
	clients:    make(map[*websocket.Conn]chan FileChange),
	broadcast:  make(chan FileChange),
	register:   make(chan *websocket.Conn),
	unregister: make(chan *websocket.Conn),
}

// Add this function to run the notifier
func (n *FileChangeNotifier) run() {
	for {
		select {
		case client := <-n.register:
			n.mutex.Lock()
			n.clients[client] = make(chan FileChange, 10)
			n.mutex.Unlock()
			log.Printf("Client registered, total clients: %d", len(n.clients))

		case client := <-n.unregister:
			n.mutex.Lock()
			if ch, ok := n.clients[client]; ok {
				close(ch)
				delete(n.clients, client)
			}
			n.mutex.Unlock()
			log.Printf("Client unregistered, total clients: %d", len(n.clients))

		case change := <-n.broadcast:
			n.mutex.RLock()
			for client, ch := range n.clients {
				select {
				case ch <- change:
				default:
					close(ch)
					delete(n.clients, client)
				}
			}
			n.mutex.RUnlock()
		}
	}
}

// Add this function to send changes to clients
func (n *FileChangeNotifier) sendToClient(conn *websocket.Conn) {
	ch, ok := n.clients[conn]
	if !ok {
		return
	}

	for change := range ch {
		err := conn.WriteJSON(change)
		if err != nil {
			log.Printf("Error sending to client: %v", err)
			n.unregister <- conn
			return
		}
	}
}

func handleTestConnection(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	w.Write([]byte(`{"status": "ok"}`))
}

type TreeNode struct {
	Name     string      `json:"name"`
	Path     string      `json:"path"`
	Icon     string      `json:"icon"`
	IsDir    bool        `json:"isDir"`
	Children []*TreeNode `json:"children,omitempty"`
}

func buildFileTree(files []models.FileMetadata) []*TreeNode {
	root := make(map[string]*TreeNode)
	
	for _, file := range files {
		parts := strings.Split(file.Path, string(os.PathSeparator))
		currentPath := ""
		var currentNode *TreeNode
		
		for i, part := range parts {
			if part == "" {
				continue
			}
			
			if currentPath != "" {
				currentPath = filepath.Join(currentPath, part)
			} else {
				currentPath = part
			}
			
			if node, exists := root[currentPath]; exists {
				currentNode = node
				continue
			}
			
			isDir := i < len(parts)-1 || file.IsDir
			node := &TreeNode{
				Name:     part,
				Path:     currentPath,
				Icon:     getFileIcon(part, isDir),
				IsDir:    isDir,
				Children: make([]*TreeNode, 0),
			}
			
			root[currentPath] = node
			
			if currentNode != nil {
				currentNode.Children = append(currentNode.Children, node)
			}
			currentNode = node
		}
	}
	
	// Get only root level nodes
	var result []*TreeNode
	for _, node := range root {
		isRoot := !strings.Contains(node.Path, string(os.PathSeparator))
		if isRoot {
			result = append(result, node)
		}
	}
	
	// Sort nodes (directories first, then alphabetically)
	sortNodes(result)
	return result
}

func sortNodes(nodes []*TreeNode) {
	sort.Slice(nodes, func(i, j int) bool {
		// Directories come before files
		if nodes[i].IsDir != nodes[j].IsDir {
			return nodes[i].IsDir
		}
		// Alphabetical order within same type
		return strings.ToLower(nodes[i].Name) < strings.ToLower(nodes[j].Name)
	})
	
	// Sort children recursively
	for _, node := range nodes {
		if len(node.Children) > 0 {
			sortNodes(node.Children)
		}
	}
}

func getFileIcon(name string, isDir bool) string {
	if isDir {
		return "📁"
	}
	
	ext := strings.ToLower(filepath.Ext(name))
	switch ext {
	case ".txt", ".md":
		return "📄"
	case ".pdf":
		return "📕"
	case ".jpg", ".jpeg", ".png", ".gif":
		return "🖼️"
	case ".mp3", ".wav", ".ogg":
		return "🎵"
	case ".mp4", ".mov", ".avi":
		return "🎬"
	default:
		return "📄"
	}
}

func handleFileList(w http.ResponseWriter, r *http.Request) {
	log.Printf("=== File List Request Started ===")
	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	log.Printf("Using base path: %s", basePath)
	
	// Get username from context
	username, ok := r.Context().Value("username").(string)
	if !ok {
		log.Printf("ERROR: No username in context")
		http.Error(w, "Unauthorized", http.StatusUnauthorized)
		return
	}
	
	// Use username-specific path
	userBasePath := filepath.Join(basePath, username)
	log.Printf("Using user path: %s", userBasePath)
	
	// Check if directory exists
	if _, err := os.Stat(userBasePath); err != nil {
		if os.IsNotExist(err) {
			log.Printf("Creating new user directory: %s", userBasePath)
			if err := os.MkdirAll(userBasePath, 0755); err != nil {
				log.Printf("Error creating directory: %v", err)
				http.Error(w, "Failed to create directory", http.StatusInternalServerError)
				return
			}
			// Return empty response for new directory
			log.Printf("Returning empty response for new directory")
			w.Header().Set("Content-Type", "application/json")
			if r.Header.Get("X-Client") == "web" {
				json.NewEncoder(w).Encode(map[string]interface{}{
					"files": []models.FileMetadata{},
				})
			} else {
				json.NewEncoder(w).Encode(map[string][]models.FileMetadata{})
			}
			return
		} else {
			log.Printf("Error accessing path: %v", err)
			http.Error(w, "Failed to access file storage", http.StatusInternalServerError)
			return
		}
	}
	
	log.Printf("Walking directory: %s", userBasePath)
	var files []models.FileMetadata
	err := filepath.Walk(userBasePath, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			log.Printf("Walk error at path %s: %v", path, err)
			return err
		}
		
		// Get relative path from base path
		relativePath, err := filepath.Rel(userBasePath, path)
		if err != nil {
			log.Printf("Error getting relative path for %s: %v", path, err)
			return err
		}
		
		// Skip base directory, hidden files/directories, and .versions directories
		if relativePath == "." || 
		   strings.HasPrefix(relativePath, ".") || 
		   strings.Contains(relativePath, "/.versions/") ||
		   strings.Contains(relativePath, "\\.versions\\") {
			log.Printf("Skipping path: %s", relativePath)
			return nil
		}
		
		log.Printf("Processing file: %s", relativePath)
		metadata, err := models.NewFileMetadata(userBasePath, relativePath)
		if err != nil {
			log.Printf("Error creating metadata for %s: %v", relativePath, err)
			return err
		}
		
		log.Printf("Adding file to list: %s (isDir: %v)", metadata.Path, metadata.IsDir)
		files = append(files, *metadata)
		return nil
	})

	if err != nil {
		log.Printf("Error walking file tree: %v", err)
		http.Error(w, err.Error(), http.StatusInternalServerError)
		return
	}

	log.Printf("Found %d files", len(files))

	// Check if the request is from the web UI by looking for the "X-Client" header
	if r.Header.Get("X-Client") == "web" {
		log.Printf("Sending web response with %d files", len(files))
		w.Header().Set("Content-Type", "application/json")
		w.Header().Set("Cache-Control", "no-cache")
		response := map[string]interface{}{
			"files": files,
		}
		if err := json.NewEncoder(w).Encode(response); err != nil {
			log.Printf("Error encoding response: %v", err)
			http.Error(w, "Failed to encode response", http.StatusInternalServerError)
			return
		}
		return
	}

	// For desktop client, return the flat list grouped by directory
	filesByDir := make(map[string][]models.FileMetadata)
	for _, file := range files {
		dir := filepath.Dir(file.Path)
		if dir == "." {
			dir = ""
		}
		filesByDir[dir] = append(filesByDir[dir], file)
	}
	
	log.Printf("Successfully processed %d files in %d directories", len(files), len(filesByDir))
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-cache")
	json.NewEncoder(w).Encode(filesByDir)
}

func handleFileUpload(w http.ResponseWriter, r *http.Request) {
	log.Printf("=== File Upload Request Started ===")
	log.Printf("Method: %s, URL: %s", r.Method, r.URL.String())
	
	// Parse multipart form with 32MB max memory
	if err := r.ParseMultipartForm(32 << 20); err != nil {
		log.Printf("ERROR: Failed to parse form: %v", err)
		http.Error(w, "Failed to parse form", http.StatusBadRequest)
		return
	}
	
	log.Printf("Form parsed successfully")
	
	file, header, err := r.FormFile("file")
	if err != nil {
		log.Printf("ERROR: Failed to get file from form: %v", err)
		http.Error(w, "Failed to get file from form", http.StatusBadRequest)
		return
	}
	defer file.Close()
	
	log.Printf("File received: %s", header.Filename)
	
	// Get the relative path from form
	relativePath := r.FormValue("relativePath")
	if relativePath == "" {
		relativePath = header.Filename
	}
	log.Printf("Relative path: %s", relativePath)
	
	// Dump all form values for debugging
	log.Printf("=== Form Values ===")
	for key, values := range r.Form {
		log.Printf("Key: %s, Values: %v", key, values)
	}
	
	// Add debug logging before path safety check
	log.Printf("Checking path safety for: %q", relativePath)
	
	// Ensure the path is safe
	if !isPathSafe(relativePath) {
		log.Printf("ERROR: Path rejected as unsafe: %q", relativePath)
		http.Error(w, "Invalid path", http.StatusBadRequest)
		return
	}
	
	log.Printf("Path validated as safe: %q", relativePath)
	
	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	username := r.Context().Value("username").(string)
	userBasePath := filepath.Join(basePath, username)
	targetPath := filepath.Join(userBasePath, relativePath)
	log.Printf("Full path: %q", targetPath)
	
	// Add debug logging
	log.Printf("Creating directory: %s", filepath.Dir(targetPath))
	
	// Ensure directory exists with more permissive permissions
	dirPath := filepath.Dir(targetPath)
	if err := os.MkdirAll(dirPath, 0755); err != nil {
		log.Printf("Directory creation failed: %v", err)
		http.Error(w, fmt.Sprintf("Failed to create directories: %v", err), http.StatusInternalServerError)
		return
	}

	// Add debug logging
	log.Printf("Directory created successfully: %s", dirPath)

	// Create the file with explicit permissions
	dst, err := os.OpenFile(targetPath, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0644)
	if err != nil {
		log.Printf("ERROR: Failed to create file: %v", err)
		http.Error(w, "Failed to create file", http.StatusInternalServerError)
		return
	}
	defer dst.Close()

	// Copy the file contents
	if _, err := io.Copy(dst, file); err != nil {
		log.Printf("ERROR: Failed to copy file contents: %v", err)
		http.Error(w, "Failed to save file", http.StatusInternalServerError)
		return
	}

	log.Printf("File uploaded successfully: %s", targetPath)
	w.WriteHeader(http.StatusOK)
}

func handleFileDownload(w http.ResponseWriter, r *http.Request) {
	vars := mux.Vars(r)
	relativePath := vars["filePath"]

	if !isPathSafe(relativePath) {
		http.Error(w, "Invalid path", http.StatusBadRequest)
		return
	}

	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	username := r.Context().Value("username").(string)
	userBasePath := filepath.Join(basePath, username)
	filePath := filepath.Join(userBasePath, relativePath)

	// Get file info
	info, err := os.Stat(filePath)
	if err != nil {
		if os.IsNotExist(err) {
			http.Error(w, "File not found", http.StatusNotFound)
		} else {
			http.Error(w, "Failed to access file", http.StatusInternalServerError)
		}
		return
	}

	// Don't allow directory downloads
	if info.IsDir() {
		http.Error(w, "Cannot download directories", http.StatusBadRequest)
		return
	}

	// Open the file
	file, err := os.Open(filePath)
	if err != nil {
		http.Error(w, "Failed to open file", http.StatusInternalServerError)
		return
	}
	defer file.Close()

	// Set headers
	w.Header().Set("Content-Type", "application/octet-stream")
	w.Header().Set("Content-Disposition", fmt.Sprintf(`attachment; filename="%s"`, filepath.Base(relativePath)))
	w.Header().Set("Content-Length", fmt.Sprintf("%d", info.Size()))

	// Stream the file
	if _, err := io.Copy(w, file); err != nil {
		log.Printf("Error streaming file: %v", err)
	}
}

// Helper function to prevent directory traversal attacks
func isPathSafe(path string) bool {
	log.Printf("=== Path Safety Check ===")
	log.Printf("Checking path: %q", path)
	
	// Check for directory traversal attempts
	if strings.Contains(path, "..") {
		log.Printf("REJECTED: Path contains ..")
		return false
	}
	
	// Check for absolute paths
	if strings.HasPrefix(path, "/") {
		log.Printf("REJECTED: Path is absolute")
		return false
	}
	
	// Split path into segments and check each one
	cleanPath := filepath.Clean(path)
	log.Printf("Cleaned path: %q", cleanPath)
	segments := strings.Split(cleanPath, string(filepath.Separator))
	log.Printf("Path segments: %v", segments)
	
	for i, segment := range segments {
		log.Printf("Checking segment %d: %q", i, segment)
		if segment == ".." {
			log.Printf("REJECTED: Segment is ..")
			return false
		}
	}
	
	log.Printf("Path is safe")
	return true
}

func handleHealthCheck(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	w.Write([]byte(`{"status": "healthy"}`))
}

// Add these message types
type SyncMessage struct {
	Type    string      `json:"type"`
	Path    string      `json:"path,omitempty"`
	Status  string      `json:"status,omitempty"`
	Files   []models.FileMetadata `json:"files,omitempty"`
}

// Add this function to get the file list (reusing logic from handleFileList)
func getFileList() ([]models.FileMetadata, error) {
	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	
	var files []models.FileMetadata
	err := filepath.Walk(basePath, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		
		// Get relative path from base path
		relativePath, err := filepath.Rel(basePath, path)
		if err != nil {
			return err
		}
		
		// Skip base directory
		if relativePath == "." {
			return nil
		}
		
		metadata, err := models.NewFileMetadata(basePath, relativePath)
		if err != nil {
			return err
		}
		
		files = append(files, *metadata)
		return nil
	})

	return files, err
}

// Update the upgrader configuration
var upgrader = websocket.Upgrader{
	ReadBufferSize:  1024,
	WriteBufferSize: 1024,
	CheckOrigin: func(r *http.Request) bool {
		return true
	},
	EnableCompression: false,  // Disable compression for compatibility
	HandshakeTimeout: 10 * time.Second,
}

const (
	// Time allowed to write a message to the peer
	writeWait = 10 * time.Second
	
	// Time allowed to read the next pong message from the peer
	pongWait = 60 * time.Second
	
	// Send pings to peer with this period. Must be less than pongWait
	pingPeriod = (pongWait * 9) / 10
)

func handleFileChanges(w http.ResponseWriter, r *http.Request) {
	log.Printf("=== WebSocket Connection Request ===")
	log.Printf("Headers: %v", r.Header)
	
	// Try session auth first
	sess, err := session.GetSession(r)
	var username string
	var ok bool
	
	if err == nil {
		username, ok = sess.Values["username"].(string)
		log.Printf("Session auth result: ok=%v, username=%s", ok, username)
	}
	
	// If session auth fails, try basic auth
	if !ok || username == "" {
		username, password, hasAuth := r.BasicAuth()
		log.Printf("Basic auth provided: %v, username: %s", hasAuth, username)
		
		if !hasAuth || !config.ValidateCredentials(username, password) {
			log.Printf("ERROR: Basic auth validation failed for user: %s", username)
			http.Error(w, "Unauthorized", http.StatusUnauthorized)
			return
		}
		log.Printf("Basic auth successful for user: %s", username)
	}

	if username == "" {
		log.Printf("ERROR: No valid authentication found")
		http.Error(w, "Unauthorized", http.StatusUnauthorized)
		return
	}

	log.Printf("Authenticated user: %s", username)
	
	// Store username in context for later use
	ctx := context.WithValue(r.Context(), "username", username)
	r = r.WithContext(ctx)
	
	// Upgrade connection to WebSocket
	conn, err := upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Printf("ERROR: WebSocket upgrade failed: %v", err)
		return
	}
	
	log.Printf("WebSocket connection established for user: %s", username)
	
	// Register client
	notifier.register <- conn
	
	// Ensure cleanup on exit
	defer func() {
		notifier.unregister <- conn
		conn.Close()
	}()
	
	// Start sending changes to this client
	go notifier.sendToClient(conn)
	
	// Keep connection alive with ping/pong
	conn.SetReadDeadline(time.Now().Add(pongWait))
	conn.SetPongHandler(func(string) error {
		conn.SetReadDeadline(time.Now().Add(pongWait))
		return nil
	})
	
	// Start ping ticker
	go func() {
		ticker := time.NewTicker(pingPeriod)
		defer ticker.Stop()
		
		for {
			select {
			case <-ticker.C:
				if err := conn.WriteControl(websocket.PingMessage, []byte{}, time.Now().Add(writeWait)); err != nil {
					return
				}
			}
		}
	}()
	
	// Read messages (to keep connection alive)
	for {
		if _, _, err := conn.ReadMessage(); err != nil {
			if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
				log.Printf("Error reading message: %v", err)
			}
			break
		}
	}
}

// Add this helper function
func getUserFiles(username, userBasePath string) ([]models.FileMetadata, error) {
	log.Printf("Getting files for user %s from %s", username, userBasePath)
	
	var files []models.FileMetadata
	err := filepath.Walk(userBasePath, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		
		// Get relative path from user's base path
		relativePath, err := filepath.Rel(userBasePath, path)
		if err != nil {
			return err
		}
		
		// Skip base directory
		if relativePath == "." {
			return nil
		}
		
		metadata, err := models.NewFileMetadata(userBasePath, relativePath)
		if err != nil {
			return err
		}
		
		files = append(files, *metadata)
		return nil
	})

	if err != nil {
		return nil, err
	}

	log.Printf("Found %d files for user %s", len(files), username)
	return files, nil
}

// Add this logging middleware
func loggingMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		log.Printf("Request: %s %s", r.Method, r.URL.Path)
		next.ServeHTTP(w, r)
	})
}

// Add this function to handle file deletions
func handleFileDelete(w http.ResponseWriter, r *http.Request) {
	vars := mux.Vars(r)
	relativePath := vars["filePath"]
	
	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	username := r.Context().Value("username").(string)
	userBasePath := filepath.Join(basePath, username)
	filePath := filepath.Join(userBasePath, relativePath)
	
	// Get metadata before deletion for notification
	metadata, err := models.NewFileMetadata(userBasePath, relativePath)
	if err == nil {
		// Only notify if file existed
		defer func() {
			notifier.broadcast <- FileChange{
				Type: "delete",
				FileMetadata: *metadata,
			}
		}()
	}
	
	// Delete the file
	err = os.Remove(filePath)
	if err != nil {
		http.Error(w, "Failed to delete file", http.StatusInternalServerError)
		return
	}
	
	w.WriteHeader(http.StatusOK)
}

// Add this new endpoint in main()
func handleDirectoryCreate(w http.ResponseWriter, r *http.Request) {
	log.Printf("=== Directory Creation Request Started ===")
	
	// Parse the form data
	if err := r.ParseForm(); err != nil {
		log.Printf("ERROR: Failed to parse form: %v", err)
		http.Error(w, "Failed to parse form", http.StatusBadRequest)
		return
	}
	
	relativePath := r.FormValue("path")
	if relativePath == "" {
		log.Printf("ERROR: No path provided")
		http.Error(w, "Path is required", http.StatusBadRequest)
		return
	}
	
	if !isPathSafe(relativePath) {
		log.Printf("ERROR: Path rejected as unsafe: %q", relativePath)
		http.Error(w, "Invalid path", http.StatusBadRequest)
		return
	}
	
	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	username := r.Context().Value("username").(string)
	userBasePath := filepath.Join(basePath, username)
	fullPath := filepath.Join(userBasePath, relativePath)
	
	if err := os.MkdirAll(fullPath, 0777); err != nil {
		log.Printf("ERROR: Failed to create directory: %v", err)
		http.Error(w, "Failed to create directory", http.StatusInternalServerError)
		return
	}
	
	// Get metadata for response
	metadata, err := models.NewFileMetadata(userBasePath, relativePath)
	if err != nil {
		http.Error(w, "Failed to get directory metadata", http.StatusInternalServerError)
		return
	}
	
	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(metadata)
}

func init() {
	// Add this near the top of main() or in an init() function
	log.SetFlags(log.Ldate | log.Ltime | log.Lmicroseconds | log.Lshortfile)
}

type Server struct {
	configService *services.ConfigService
	router       *mux.Router
}

func NewServer() *Server {
	return &Server{
		router: mux.NewRouter(),
	}
}

func (s *Server) handleConfig(w http.ResponseWriter, r *http.Request) {
	switch r.Method {
	case "GET":
		s.getConfig(w, r)
	case "POST":
		s.updateConfig(w, r)
	default:
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
	}
}

func (s *Server) getConfig(w http.ResponseWriter, r *http.Request) {
	username, ok := r.Context().Value("username").(string)
	if !ok {
		log.Printf("ERROR: No username in context for config request")
		http.Error(w, "Unauthorized: missing username", http.StatusUnauthorized)
		return
	}

	log.Printf("Loading config for user: %s", username)
	config, err := s.configService.LoadConfig(username)
	if err != nil {
		log.Printf("ERROR: Failed to load config: %v", err)
		http.Error(w, "Failed to read configuration", http.StatusInternalServerError)
		return
	}

	log.Printf("Successfully loaded config for user: %s", username)
	w.Header().Set("Content-Type", "application/json")
	json.NewEncoder(w).Encode(config)
}

func (s *Server) updateConfig(w http.ResponseWriter, r *http.Request) {
	username, ok := r.Context().Value("username").(string)
	if !ok {
		http.Error(w, "Unauthorized: missing username", http.StatusUnauthorized)
		return
	}

	var config models.Config
	if err := json.NewDecoder(r.Body).Decode(&config); err != nil {
		http.Error(w, "Invalid configuration data", http.StatusBadRequest)
		return
	}

	if err := s.configService.SaveConfig(username, &config); err != nil {
		http.Error(w, "Failed to save configuration", http.StatusInternalServerError)
		return
	}

	w.WriteHeader(http.StatusOK)
}

// getUserBasePath returns the base path for a user's files
func getUserBasePath(username string) string {
	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	return filepath.Join(basePath, username)
}

func handleFileSave(w http.ResponseWriter, r *http.Request) {
	vars := mux.Vars(r)
	relativePath := vars["filePath"]

	if !isPathSafe(relativePath) {
		http.Error(w, "Invalid path", http.StatusBadRequest)
		return
	}

	username := r.Context().Value("username").(string)
	if username == "" {
		http.Error(w, "Unauthorized", http.StatusUnauthorized)
		return
	}

	basePath := config.GetEnvOrDefault("SYNC_STORAGE_PATH", "/app/data")
	userBasePath := filepath.Join(basePath, username)
	filePath := filepath.Join(userBasePath, relativePath)

	// Ensure directory exists
	dirPath := filepath.Dir(filePath)
	if err := os.MkdirAll(dirPath, 0755); err != nil {
		log.Printf("Error creating directory: %v", err)
		http.Error(w, "Failed to create directory", http.StatusInternalServerError)
		return
	}

	// Read the request body
	content, err := io.ReadAll(r.Body)
	if err != nil {
		log.Printf("Error reading request body: %v", err)
		http.Error(w, "Failed to read request body", http.StatusInternalServerError)
		return
	}

	// Write the file
	if err := os.WriteFile(filePath, content, 0644); err != nil {
		log.Printf("Error writing file: %v", err)
		http.Error(w, "Failed to write file", http.StatusInternalServerError)
		return
	}

	log.Printf("File saved successfully: %s", filePath)

	// Get updated file metadata and broadcast change
	metadata, err := models.NewFileMetadata(userBasePath, relativePath)
	if err != nil {
		log.Printf("Error getting file metadata after save: %v", err)
	} else {
		notifier.broadcast <- FileChange{
			Type: "update",
			FileMetadata: *metadata,
		}
	}

	w.WriteHeader(http.StatusOK)
}

func main() {
	// Initialize auth configuration
	config.InitAuth()
	
	// Initialize session store
	session.InitStore([]byte(config.GetEnvOrDefault("SESSION_KEY", "change-me-in-production")))
	
	server := NewServer()
	router := server.router
	router.Use(loggingMiddleware)
	
	// Add public endpoints first (no auth required)
	router.HandleFunc("/api/login", handleLogin).Methods("POST")
	router.HandleFunc("/api/logout", handleLogout).Methods("POST")
	router.HandleFunc("/health", handleHealthCheck).Methods("GET")
	router.HandleFunc("/api/changes", handleFileChanges)
	
	// Create API subrouter with authentication
	apiRouter := router.PathPrefix("/api").Subrouter()
	apiRouter.Use(middleware.BasicAuth)
	
	// Add authenticated API endpoints
	apiRouter.HandleFunc("/test", handleTestConnection).Methods("GET")
	apiRouter.HandleFunc("/files", handleFileList).Methods("GET")
	apiRouter.HandleFunc("/files", handleFileUpload).Methods("POST")
	apiRouter.HandleFunc("/files/{filePath:.*}", handleFileDownload).Methods("GET")
	apiRouter.HandleFunc("/files/{filePath:.*}", handleFileDelete).Methods("DELETE")
	apiRouter.HandleFunc("/files/{filePath:.*}", handleFileSave).Methods("PUT")
	apiRouter.HandleFunc("/directories", handleDirectoryCreate).Methods("POST")
	apiRouter.HandleFunc("/config", server.handleConfig).Methods("GET", "POST")
	
	// Add user info endpoint
	apiRouter.HandleFunc("/user", func(w http.ResponseWriter, r *http.Request) {
		sess, err := session.GetSession(r)
		if err != nil {
			http.Error(w, "Unauthorized", http.StatusUnauthorized)
			return
		}

		username, ok := sess.Values["username"].(string)
		if !ok {
			http.Error(w, "Unauthorized", http.StatusUnauthorized)
			return
		}

		w.Header().Set("Content-Type", "application/json")
		json.NewEncoder(w).Encode(map[string]string{
			"username": username,
		})
	}).Methods("GET")

	// All other routes should serve the SPA's index.html
	router.PathPrefix("/").HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		// Don't interfere with API routes
		if strings.HasPrefix(r.URL.Path, "/api/") {
			http.NotFound(w, r)
			return
		}
		
		// Serve static files
		if strings.HasPrefix(r.URL.Path, "/_app/") {
			http.StripPrefix("/_app/", http.FileServer(http.Dir("public/_app"))).ServeHTTP(w, r)
			return
		}
		
		if strings.HasPrefix(r.URL.Path, "/assets/") {
			http.StripPrefix("/assets/", http.FileServer(http.Dir("public/assets"))).ServeHTTP(w, r)
			return
		}
		
		// Serve index.html for all other routes
		log.Printf("Serving index.html for path: %s", r.URL.Path)
		http.ServeFile(w, r, "public/index.html")
	})
	
	// Start the file change notifier
	go notifier.run()
	
	// Create and start the server
	srv := &http.Server{
		Handler:      router,
		Addr:         "0.0.0.0:8080",  // Explicitly bind to all interfaces
		WriteTimeout: 15 * time.Second,
		ReadTimeout:  15 * time.Second,
	}

	log.Printf("Starting server on http://%s", srv.Addr)
	log.Fatal(srv.ListenAndServe())
}

func handleLogin(w http.ResponseWriter, r *http.Request) {
	log.Printf("=== Login Request ===")
	var credentials struct {
		Username string `json:"username"`
		Password string `json:"password"`
	}

	if err := json.NewDecoder(r.Body).Decode(&credentials); err != nil {
		log.Printf("Invalid request body: %v", err)
		http.Error(w, "Invalid request", http.StatusBadRequest)
		return
	}

	log.Printf("Attempting login for user: %s", credentials.Username)
	if config.ValidateCredentials(credentials.Username, credentials.Password) {
		log.Printf("Login successful for user: %s", credentials.Username)
		sess, err := session.GetSession(r)
		if err != nil {
			log.Printf("Session error: %v", err)
			http.Error(w, "Session error", http.StatusInternalServerError)
			return
		}
		sess.Values["username"] = credentials.Username
		if err := session.SaveSession(sess, w, r); err != nil {
			log.Printf("Failed to save session: %v", err)
			http.Error(w, "Failed to save session", http.StatusInternalServerError)
			return
		}
		w.WriteHeader(http.StatusOK)
	} else {
		log.Printf("Invalid credentials for user: %s", credentials.Username)
		http.Error(w, "Invalid credentials", http.StatusUnauthorized)
	}
}

func handleLogout(w http.ResponseWriter, r *http.Request) {
	sess, err := session.GetSession(r)
	if err != nil {
		http.Error(w, "Session error", http.StatusInternalServerError)
		return
	}
	sess.Values = map[interface{}]interface{}{}
	if err := session.SaveSession(sess, w, r); err != nil {
		http.Error(w, "Failed to save session", http.StatusInternalServerError)
		return
	}
	w.WriteHeader(http.StatusOK)
} 