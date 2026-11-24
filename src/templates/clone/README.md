# Archive (read-only 1:1 clone)

This folder was prepared by `eqlmaker clone`. It becomes the archive of the original application:
its masks as they looked, with live data, without any way to change data.

- `reference/` - reference pictures of the masks, from the recording.
- `public/<mask>.html` - one page per mask. The reference picture is the backdrop; every recorded
  control is an element positioned on top (`class="ctl button|list|field"`, `data-text`).
- `public/clone.css`, `public/clone.js` - style and helpers (`archive.query(name, args)`).
- `queries.js` - the SELECT statements of the archive, by name.
- `server.js` - static files and `/query/<name>`; every query runs in a `READ ONLY` transaction
  and only single statements that start with `SELECT` or `WITH` are accepted.
- `CLONE.md` - the instructions for filling the pages.

Start: `npm install`, then `node server.js` with the database in the environment variable named
in `server.js` and `PORT` (default 8790). The server listens on 127.0.0.1 only.

Controls with the class `archive-off` are greyed out and cannot be operated: they changed data in
the original.
