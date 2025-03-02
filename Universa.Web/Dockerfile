# Build Go stage
FROM golang:1.21-alpine AS builder
WORKDIR /app

# Install git for module downloads
RUN apk add --no-cache git

# Copy go.mod and initialize modules
COPY go.mod .
RUN go mod download && \
    go get github.com/gorilla/mux@v1.8.1 && \
    go get github.com/gorilla/sessions@v1.2.2 && \
    go get github.com/gorilla/websocket@v1.5.1 && \
    go mod tidy

# Copy the source code
COPY . .

# Build the application
RUN go mod tidy && \
    CGO_ENABLED=0 GOOS=linux go build -o universa-web

# Final stage
FROM alpine:latest
WORKDIR /app

# Create user with specific UID/GID
RUN addgroup -g 1000 -S universa && \
    adduser -u 1000 -S universa -G universa

# Create and set permissions on data directory
RUN mkdir -p /app/data && \
    chown -R universa:universa /app/data && \
    chmod -R 755 /app/data

# Copy the binary and .env file
COPY --from=builder /app/universa-web .
COPY .env .

# Set ownership of all files
RUN chown -R universa:universa /app

USER universa

EXPOSE 8080
CMD ["./universa-web"] 