# Build stage for Svelte app
FROM node:20-alpine AS ui-builder
WORKDIR /app/ui
# Copy package files first for better caching
COPY ui/package*.json ./
RUN npm install --legacy-peer-deps
# Then copy the rest of the UI files
COPY ui/ .
# Build the UI
RUN npm run build && \
    echo "=== SvelteKit Build Output ===" && \
    find .svelte-kit/output -type f && \
    echo "=== Client Directory ===" && \
    ls -la .svelte-kit/output/client

# Build stage for Go app
FROM golang:1.21-alpine AS go-builder
WORKDIR /app
COPY . .
RUN go mod tidy && go mod download
RUN go build -o main .

# Final stage
FROM alpine:latest
WORKDIR /app
COPY --from=go-builder /app/main .
# Create public directory and copy static files
RUN mkdir -p /app/public
COPY --from=ui-builder /app/ui/.svelte-kit/output/client/. /app/public/
# Copy the index.html file
COPY public/index.html /app/public/

# Verify the final structure
RUN echo "=== Final Directory Structure ===" && \
    find /app/public -type f

EXPOSE 8080
CMD ["./main"] 