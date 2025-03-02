# Universa.Web

Universa.Web is a web service that provides synchronization and authentication services for the Universa Desktop application. It is designed to enable seamless file synchronization, user authentication, and configuration management across multiple devices.

## Features

- User authentication and authorization
- Real-time file synchronization via WebSocket
- Secure file storage and version history
- Configuration synchronization
- RESTful API endpoints
- PostgreSQL database for user data
- Docker support for easy deployment

## Prerequisites

- Go 1.21 or later
- PostgreSQL 16 or later
- Docker and Docker Compose (for containerized deployment)

## Getting Started

### Local Development

1. Clone the repository:
   ```bash
   git clone https://github.com/yourusername/universa.web.git
   cd universa.web
   ```

2. Install dependencies:
   ```bash
   go mod download
   ```

3. Set up environment variables:
   ```bash
   export PORT=8080
   export DB_HOST=localhost
   export DB_PORT=5432
   export DB_USER=postgres
   export DB_PASSWORD=postgres
   export DB_NAME=universa
   export DB_SSLMODE=disable
   export JWT_SECRET=your-secret-key
   export FILE_STORAGE_PATH=./data
   ```

4. Run the server:
   ```bash
   go run cmd/server/main.go
   ```

### Docker Deployment

1. Build and start the containers:
   ```bash
   cd deployments/docker
   docker-compose up -d
   ```

2. Check the logs:
   ```bash
   docker-compose logs -f
   ```

3. Stop the containers:
   ```bash
   docker-compose down
   ```

## API Documentation

### Authentication

- `POST /api/auth/register` - Register a new user
- `POST /api/auth/login` - Login and receive JWT token

### File Operations

- `GET /api/files/{path}` - Get file content
- `PUT /api/files/{path}` - Upload or update file
- `DELETE /api/files/{path}` - Delete file
- `GET /api/files/{path}/info` - Get file metadata

### WebSocket

- Connect to `/ws` for real-time synchronization
- Message types:
  - `sync` - File synchronization
  - `ack` - Acknowledgment
  - `error` - Error message
  - `ping/pong` - Connection health check

## Development

### Project Structure

```
.
├── cmd/
│   └── server/          # Main application entry point
├── internal/
│   ├── api/            # API handlers and middleware
│   ├── auth/           # Authentication and authorization
│   ├── models/         # Data models
│   ├── storage/        # Storage implementations
│   └── sync/           # Synchronization logic
├── pkg/
│   ├── client/         # Client library
│   └── protocol/       # Sync protocol definitions
└── deployments/
    └── docker/         # Docker configurations
```

### Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request

## License

This project is licensed under the MIT License - see the LICENSE file for details. 