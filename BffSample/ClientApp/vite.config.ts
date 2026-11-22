import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import os from 'os';
import { env } from 'process';
import { execFileSync } from 'child_process';

const certFolder = (env.APPDATA ?? '') !== ''
  ? path.join(env.APPDATA!, 'ASP.NET', 'https')
  : path.join(os.homedir(), '.aspnet', 'https');

// Define the paths
const certPath = path.resolve(certFolder, `localhost.pem`);
const keyPath = path.resolve(certFolder, `localhost.key`);

// Export the PEM file only if it doesn't exist. The path goes in as one argument, not through a shell, so a
// profile folder with a space in its name stays one path.
if (!fs.existsSync(certPath) || !fs.existsSync(keyPath)) {
  console.log(`Certificate or key file not found. Generating new files (${certPath}, ${keyPath})...`);
  // dotnet dev-certs refuses to export into a folder that does not exist, which it does not on a fresh machine.
  // The folder receives an unencrypted private key, so only its owner may read it (Windows ignores the mode).
  fs.mkdirSync(certFolder, { recursive: true, mode: 0o700 });
  execFileSync('dotnet', ['dev-certs', 'https', '--export-path', certPath, '--format', 'Pem', '--no-password'],
    { stdio: 'inherit' });
} else {
  console.log('Certificate and key files already exist. Skipping generation.');
}

export default defineConfig({
  plugins: [react()],
  server: {
    port: 3000,
    // Listening on every interface would let anyone on the same network (a cafe, a shared office) open the
    // dev server and read whatever it serves: the app's source, and any file a dev server flaw lets out of the
    // project. The BFF's SPA proxy only needs localhost.
    host: 'localhost',
    https: {
      // Read the contents of the PEM and key files
      cert: fs.readFileSync(certPath, 'utf-8'),
      key: fs.readFileSync(keyPath, 'utf-8'),
    },
  },
})
