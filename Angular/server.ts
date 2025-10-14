import 'zone.js/node';
import express from 'express';
import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { render } from './main.server'; // <-- функция renderApplication

const currentDir = dirname(fileURLToPath(import.meta.url));
const DIST_FOLDER = join(currentDir, '../browser');
const INDEX_HTML = join(DIST_FOLDER, 'index.html');
const PORT = process.env['PORT'] || 4000;

console.log('hhh1');

async function run() {

  console.log('hhh');
  if (!existsSync(INDEX_HTML)) {
    console.warn(
      `Unable to locate an index.html file at "${INDEX_HTML}". ` +
      'Ensure the Angular browser build has been generated with `ng build` or `npm run build:ssr` before starting the server.',
    );
  }
  const server = express();

  server.use(express.static(DIST_FOLDER, { maxAge: '1y' }));

  server.get('*', async (req, res) => {
    try {
      const html = await render(req.url, INDEX_HTML);
      res.status(200).send(html);
    } catch (err) {
      res.status(500).send(err);
    }
  });

  server.listen(PORT, () => {
    console.log(`Node server listening on http://localhost:${PORT}`);
  });
}

run();
