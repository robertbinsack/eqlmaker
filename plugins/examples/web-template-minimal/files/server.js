// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Smallest possible starting point for the web application "{{APP_NAME}}": a static page.
// A real template brings database access and page structure; see the built-in template.
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');

const port = Number(process.env.PORT || 8700);
const page = () => fs.readFileSync(path.join(__dirname, 'index.html'));

const server = http.createServer((req, res) => {
  if (req.url === '/' || req.url === '/index.html') { res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' }); return res.end(page()); }
  res.writeHead(404, { 'Content-Type': 'text/plain' }); res.end('not found');
});

if (require.main === module) server.listen(port, '127.0.0.1', () => console.log('{{APP_NAME}} on http://127.0.0.1:' + port));
module.exports = { server, databaseVariable: '{{DB_ENV}}' };
