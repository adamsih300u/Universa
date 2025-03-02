import { websocketStore } from '../stores/websocket';

class WebSocketService {
    constructor() {
        this.subscribers = new Map();
        this.unsubscribe = websocketStore.subscribe(ws => {
            if (ws) {
                ws.onmessage = this.handleMessage.bind(this);
            }
        });
    }

    connect() {
        if (!websocketStore.isConnected) {
            websocketStore.connect();
        }
    }

    disconnect() {
        websocketStore.disconnect();
    }

    destroy() {
        this.unsubscribe();
        websocketStore.destroy();
        this.subscribers.clear();
    }

    subscribe(type, callback) {
        if (!this.subscribers.has(type)) {
            this.subscribers.set(type, new Set());
        }
        this.subscribers.get(type).add(callback);

        // Return unsubscribe function
        return () => this.unsubscribe(type, callback);
    }

    unsubscribe(type, callback) {
        const subscribers = this.subscribers.get(type);
        if (subscribers) {
            subscribers.delete(callback);
            if (subscribers.size === 0) {
                this.subscribers.delete(type);
            }
        }
    }

    handleMessage(event) {
        try {
            const message = JSON.parse(event.data);
            const subscribers = this.subscribers.get(message.type);
            if (subscribers) {
                subscribers.forEach(callback => {
                    try {
                        callback(message);
                    } catch (error) {
                        console.error('Error in subscriber callback:', error);
                    }
                });
            }
        } catch (error) {
            console.error('Error handling WebSocket message:', error);
        }
    }

    send(message) {
        websocketStore.send(message);
    }

    notifyFileChange(path) {
        websocketStore.notifyFileChange(path);
    }

    get isConnected() {
        return websocketStore.isConnected;
    }
}

export const websocketService = new WebSocketService(); 