import { writable, get } from 'svelte/store';

export const isAuthenticated = writable(false);
let initialized = false;

export async function checkAuthStatus() {
    if (initialized) return get(isAuthenticated);
    
    try {
        const response = await fetch('/api/user');
        const isAuthed = response.ok;
        isAuthenticated.set(isAuthed);
        initialized = true;
        return isAuthed;
    } catch (error) {
        console.error('Error checking auth status:', error);
        isAuthenticated.set(false);
        initialized = true;
        return false;
    }
}

export async function login(username, password) {
    try {
        const response = await fetch('/api/login', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({ username, password }),
        });

        if (response.ok) {
            // Store credentials for WebSocket connection
            window.localStorage.setItem('auth_username', username);
            window.localStorage.setItem('auth_password', password);
            isAuthenticated.set(true);
            initialized = true;
            return true;
        }
        return false;
    } catch (error) {
        console.error('Login error:', error);
        return false;
    }
}

export async function logout() {
    try {
        const response = await fetch('/api/logout', { method: 'POST' });
        // Clear all stored state regardless of response
        window.localStorage.clear();
        isAuthenticated.set(false);
        initialized = false;
        return response.ok;
    } catch (error) {
        console.error('Error during logout:', error);
        // Still clear state on error
        window.localStorage.clear();
        isAuthenticated.set(false);
        initialized = false;
        return false;
    }
}

// Add a function to handle unauthorized responses
export function handleUnauthorized() {
    window.localStorage.clear();
    isAuthenticated.set(false);
    initialized = false;
} 