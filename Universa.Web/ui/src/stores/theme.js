import { writable } from 'svelte/store';

// Initialize theme from localStorage or default to light
const storedTheme = typeof localStorage !== 'undefined' ? localStorage.getItem('theme') : 'light';
export const theme = writable(storedTheme || 'light');

// Subscribe to theme changes and update localStorage and CSS variables
if (typeof window !== 'undefined') {
    theme.subscribe(value => {
        localStorage.setItem('theme', value);
        if (value === 'dark') {
            document.documentElement.classList.add('dark-theme');
        } else {
            document.documentElement.classList.remove('dark-theme');
        }
    });
}

// Helper function to toggle theme
export function toggleTheme() {
    theme.update(current => current === 'light' ? 'dark' : 'light');
} 