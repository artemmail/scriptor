import 'zone.js/node'; 
import express from 'express';
import { join } from 'path';
import { render } from './main.server'; // <-- функция renderApplication

const DIST_FOLDER = join(process.cwd(), 'dist/youtube-downloader/browser');
const PORT = process.env['PORT'] || 4000;

console.log('hhh1');

async function run() {

  console.log('hhh');
  const server = express();

  server.use(express.static(DIST_FOLDER, { maxAge: '1y' }));

  server.get('*', async (req, res) => {
    try {
      const html = await render(req.url, join(DIST_FOLDER, 'index.html'));
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
