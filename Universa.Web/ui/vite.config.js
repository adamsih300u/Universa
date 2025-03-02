import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
	plugins: [
		svelte({
			compilerOptions: {
				dev: process.env.NODE_ENV !== 'production'
			}
		})
	],
	server: {
		host: true, // Listen on all network interfaces
		port: 5173,
		proxy: {
			'/api': {
				target: 'http://192.168.80.14:8080',
				changeOrigin: true,
				secure: false
			},
			'/ws': {
				target: 'ws://192.168.80.14:8080',
				ws: true,
				changeOrigin: true,
				secure: false
			}
		}
	},
	build: {
		outDir: 'dist',
		emptyOutDir: true,
		sourcemap: true,
		rollupOptions: {
			external: [
				'@sveltejs/kit',
				'@sveltejs/adapter-auto',
				'svelte-routing'
			]
		}
	}
}); 