import { createReadStream, existsSync } from "node:fs";
import { promises as fs } from "node:fs";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(__dirname, "gallery");
const port = Number(process.env.PORT || 4173);

const mimeTypes = new Map([
  [".css", "text/css; charset=utf-8"],
  [".html", "text/html; charset=utf-8"],
  [".js", "application/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".svg", "image/svg+xml"],
]);

function resolveRequestPath(urlPath) {
  const safePath = decodeURIComponent((urlPath || "/").split("?")[0]);
  const candidate = safePath === "/" ? "/index.html" : safePath;
  const resolved = path.resolve(root, `.${candidate}`);
  if (!resolved.startsWith(root)) {
    return null;
  }
  return resolved;
}

const server = http.createServer(async (req, res) => {
  const resolved = resolveRequestPath(req.url || "/");
  if (!resolved || !existsSync(resolved)) {
    res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    res.end("Not found");
    return;
  }

  const stat = await fs.stat(resolved);
  const target = stat.isDirectory() ? path.join(resolved, "index.html") : resolved;
  const ext = path.extname(target);
  res.writeHead(200, { "Content-Type": mimeTypes.get(ext) || "application/octet-stream" });
  createReadStream(target).pipe(res);
});

server.listen(port, "127.0.0.1", () => {
  console.log(`SWFOC desktop adapter gallery listening on http://127.0.0.1:${port}`);
});
