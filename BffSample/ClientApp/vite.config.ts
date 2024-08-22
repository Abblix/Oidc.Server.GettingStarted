import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import { env } from 'process';
import { execSync } from 'child_process';

const certFolder = (env.APPDATA ?? '') !== '' ? `${env.APPDATA}/ASP.NET/https` : `${env.HOME}/.aspnet/https`;

// Define the paths
const certPath = path.resolve(certFolder, `localhost.pem`);
const keyPath = path.resolve(certFolder, `localhost.key`);

// Export the PEM file only if it doesn't exist
if (!fs.existsSync(certPath) || !fs.existsSync(keyPath)) {
  console.log(`Certificate or key file not found. Generating new files (${certPath}, ${keyPath})...`);
  execSync(`dotnet dev-certs https --export-path ${certPath} --format Pem --no-password`);
} else {
  console.log('Certificate and key files already exist. Skipping generation.');
}

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    host: true,
    https: {
      // Read the contents of the PEM and key files
      cert: fs.readFileSync(certPath, 'utf-8'),
      key: fs.readFileSync(keyPath, 'utf-8'),
    },
  },
})
