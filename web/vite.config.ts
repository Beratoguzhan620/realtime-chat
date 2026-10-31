import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// In development the API runs on its own port; the dev server forwards to it like nginx does in Compose.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": "http://localhost:8080",
      "/hubs": { target: "http://localhost:8080", ws: true },
    },
  },
  build: { sourcemap: false },
});
