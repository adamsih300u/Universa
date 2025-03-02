import { writable, get } from 'svelte/store';
import { isAuthenticated } from './auth';

function createWebSocketStore() {
    const { subscribe, set } = writable(null);
    let ws = null;
    let shouldReconnect = false;
    let isConnecting = false;

    function connect() {
        if (ws || !get(isAuthenticated) || isConnecting) return;

        isConnecting = true;
        shouldReconnect = true;

        try {
            ws = new WebSocket(`ws://${window.location.hostname}:8080/api/changes`);
            
            ws.onopen = () => {
                console.log('WebSocket connected');
                set(ws);
                isConnecting = false;
            };
            
            ws.onclose = () => {
                console.log('WebSocket disconnected');
                ws = null;
                set(null);
                isConnecting = false;
                
                // Only try to reconnect if we're still authenticated and should reconnect
                if (shouldReconnect && get(isAuthenticated)) {
                    setTimeout(connect, 5000);
                }
            };
            
            ws.onerror = (error) => {
                console.error('WebSocket error:', error);
                isConnecting = false;
            };
        } catch (error) {
            console.error('Error creating WebSocket:', error);
            isConnecting = false;
        }
    }

    function send(message) {
        if (ws?.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify(message));
        } else {
            console.warn('WebSocket not connected, message not sent:', message);
        }
    }

    function notifyFileChange(path) {
        send({
            type: 'FileChanged',
            path: path
        });
    }

    function disconnect() {
        shouldReconnect = false;
        isConnecting = false;
        if (ws) {
            ws.close();
            ws = null;
            set(null);
        }
    }

    function destroy() {
        disconnect();
        shouldReconnect = false;
        isConnecting = false;
    }

    return {
        subscribe,
        connect,
        disconnect,
        destroy,
        send,
        notifyFileChange,
        get isConnected() {
            return ws?.readyState === WebSocket.OPEN;
        }
    };
}

export const websocketStore = createWebSocketStore(); 