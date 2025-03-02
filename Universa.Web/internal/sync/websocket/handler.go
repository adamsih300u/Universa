package websocket

import (
	"encoding/json"
	"log"
	"net/http"
	"sync"

	"github.com/gorilla/websocket"
)

// MessageType represents the type of WebSocket message
type MessageType string

const (
	// Message types
	TypeSync     MessageType = "sync"
	TypeAck      MessageType = "ack"
	TypeError    MessageType = "error"
	TypePing     MessageType = "ping"
	TypePong     MessageType = "pong"
)

// Message represents a WebSocket message
type Message struct {
	Type    MessageType     `json:"type"`
	Payload interface{}     `json:"payload,omitempty"`
	Error   string         `json:"error,omitempty"`
}

// Connection represents a WebSocket connection
type Connection struct {
	conn     *websocket.Conn
	userID   int64
	send     chan Message
	handler  *Handler
	mu       sync.Mutex
}

// Handler manages WebSocket connections
type Handler struct {
	upgrader  websocket.Upgrader
	connections map[int64]*Connection
	mu        sync.RWMutex
}

// NewHandler creates a new WebSocket handler
func NewHandler() *Handler {
	return &Handler{
		upgrader: websocket.Upgrader{
			ReadBufferSize:  1024,
			WriteBufferSize: 1024,
			CheckOrigin: func(r *http.Request) bool {
				// TODO: Implement proper origin checking
				return true
			},
		},
		connections: make(map[int64]*Connection),
	}
}

// HandleWebSocket upgrades the HTTP connection to WebSocket
func (h *Handler) HandleWebSocket(w http.ResponseWriter, r *http.Request, userID int64) {
	conn, err := h.upgrader.Upgrade(w, r, nil)
	if err != nil {
		log.Printf("WebSocket upgrade failed: %v", err)
		return
	}

	c := &Connection{
		conn:    conn,
		userID:  userID,
		send:    make(chan Message, 256),
		handler: h,
	}

	h.registerConnection(c)

	// Start goroutines for reading and writing
	go c.writePump()
	go c.readPump()
}

func (h *Handler) registerConnection(c *Connection) {
	h.mu.Lock()
	h.connections[c.userID] = c
	h.mu.Unlock()
}

func (h *Handler) unregisterConnection(c *Connection) {
	h.mu.Lock()
	if _, ok := h.connections[c.userID]; ok {
		delete(h.connections, c.userID)
		close(c.send)
	}
	h.mu.Unlock()
}

func (c *Connection) readPump() {
	defer func() {
		c.handler.unregisterConnection(c)
		c.conn.Close()
	}()

	for {
		_, message, err := c.conn.ReadMessage()
		if err != nil {
			if websocket.IsUnexpectedCloseError(err, websocket.CloseGoingAway, websocket.CloseAbnormalClosure) {
				log.Printf("WebSocket read error: %v", err)
			}
			break
		}

		var msg Message
		if err := json.Unmarshal(message, &msg); err != nil {
			log.Printf("Failed to unmarshal message: %v", err)
			continue
		}

		// Handle different message types
		switch msg.Type {
		case TypePing:
			c.send <- Message{Type: TypePong}
		case TypeSync:
			// TODO: Handle sync message
			c.send <- Message{Type: TypeAck}
		default:
			log.Printf("Unknown message type: %s", msg.Type)
		}
	}
}

func (c *Connection) writePump() {
	defer func() {
		c.conn.Close()
	}()

	for {
		select {
		case message, ok := <-c.send:
			if !ok {
				c.conn.WriteMessage(websocket.CloseMessage, []byte{})
				return
			}

			c.mu.Lock()
			w, err := c.conn.NextWriter(websocket.TextMessage)
			if err != nil {
				c.mu.Unlock()
				return
			}

			messageBytes, err := json.Marshal(message)
			if err != nil {
				log.Printf("Failed to marshal message: %v", err)
				c.mu.Unlock()
				continue
			}

			if _, err := w.Write(messageBytes); err != nil {
				c.mu.Unlock()
				return
			}

			if err := w.Close(); err != nil {
				c.mu.Unlock()
				return
			}
			c.mu.Unlock()
		}
	}
}

// Broadcast sends a message to all connected clients
func (h *Handler) Broadcast(message Message) {
	h.mu.RLock()
	defer h.mu.RUnlock()

	for _, c := range h.connections {
		select {
		case c.send <- message:
		default:
			h.unregisterConnection(c)
		}
	}
} 