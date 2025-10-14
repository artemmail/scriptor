import {
  createNodeRequestHandler,
  isMainModule,
} from '@angular/ssr/node';
import express from 'express';
import type { Server } from 'node:http';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const serverDistFolder = dirname(fileURLToPath(import.meta.url));
const browserDistFolder = resolve(serverDistFolder, '../browser');

const app = express();

/**
 * Example Express Rest API endpoints can be defined here.
 * Uncomment and define endpoints as necessary.
 *
 * Example:
 * ```ts
 * app.get('/api/**', (req, res) => {
 *   // Handle API request
 * });
 * ```
 */

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by serving the SPA entry point.
 */
app.get('*', (_req, res) => {
  res.sendFile(resolve(browserDistFolder, 'index.html'));
});

let serverInstance: Server | undefined;

/**
 * Starts the Express server if it hasn't been started yet.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
export function startServer(): Server {
  console.log('Starting Express server...');
  if (serverInstance) {
    return serverInstance;
  }

  const port = process.env['PORT'] || 4000;
  serverInstance = app.listen(port, () => {
    console.log(`Node Express server listening on http://localhost:${port}`);
  });

  return serverInstance;
}

/**
 * Start the server if this module is the main entry point.
 */
if (isMainModule(import.meta.url)) {
  startServer();
}

/**
 * The request handler used by the Angular CLI (dev-server and during build).
 */
export const reqHandler = createNodeRequestHandler(app);
