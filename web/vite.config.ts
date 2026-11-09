import { defineConfig } from "vite";
import vue from "@vitejs/plugin-vue";

// The UI always calls the API on the same origin under /api. In development
// and in `vite preview` (used by the e2e tests) Vite forwards it to the .NET
// API; in the container nginx does the same.
const apiTarget = process.env.API_URL ?? "http://localhost:5000";
const proxy = { "/api": { target: apiTarget, changeOrigin: true } };

export default defineConfig({
  plugins: [vue()],
  server: { port: 5173, proxy },
  preview: { port: 4173, strictPort: true, proxy },
});
