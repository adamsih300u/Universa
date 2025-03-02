import { writable } from 'svelte/store';

export const files = writable([]);
export const connectionStatus = writable('disconnected');

let ws;

export function initializeWebSocket() {
    const username = window.localStorage.getItem('auth_username');
    const password = window.localStorage.getItem('auth_password');
    
    if (!username || !password) {
        console.error('No stored credentials found');
        return;
    }

    const credentials = btoa(`${username}:${password}`);

    // Create WebSocket URL
    const wsUrl = new URL(`ws://${window.location.host}/api/changes`);
    
    // Create WebSocket with headers
    ws = new WebSocket(wsUrl.href, [], {
        headers: {
            'Authorization': `Basic ${credentials}`,
            'Upgrade': 'websocket',
            'Connection': 'Upgrade'
        }
    });
    
    // Add logging for connection states
    ws.onopen = () => {
        console.log('WebSocket connected successfully');
        connectionStatus.set('connected');
        ws.send(JSON.stringify({
            type: 'getFileList'
        }));
    };
    
    ws.onclose = (event) => {
        console.error('WebSocket closed:', event.code, event.reason);
        connectionStatus.set('disconnected');
        setTimeout(initializeWebSocket, 5000);
    };
    
    ws.onerror = (error) => {
        console.error('WebSocket error:', error);
    };
    
    ws.onmessage = (event) => {
        const message = JSON.parse(event.data);
        
        switch (message.type) {
            case 'fileList':
                files.set(message.payload.files);
                break;
                
            case 'update':
                files.update(currentFiles => {
                    const index = currentFiles.findIndex(f => f.path === message.file.path);
                    if (index >= 0) {
                        currentFiles[index] = message.file;
                    } else {
                        currentFiles.push(message.file);
                    }
                    return [...currentFiles];
                });
                break;
                
            case 'delete':
                files.update(currentFiles => 
                    currentFiles.filter(f => f.path !== message.file.path)
                );
                break;
        }
    };
}

// Add function to initialize with credentials
export function initializeWithCredentials(username, password) {
    // Store credentials for reconnection attempts
    window.localStorage.setItem('auth_username', username);
    window.localStorage.setItem('auth_password', password);
    initializeWebSocket();
} 