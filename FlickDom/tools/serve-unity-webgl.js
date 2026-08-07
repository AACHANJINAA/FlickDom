const http = require("http");
const fs = require("fs");
const path = require("path");

const root = path.resolve(process.argv[2] || process.cwd());
const port = Number.parseInt(process.argv[3] || "8080", 10);
const host = "127.0.0.1";

const contentTypes = new Map([
  [".html", "text/html; charset=utf-8"],
  [".js", "application/javascript"],
  [".wasm", "application/wasm"],
  [".data", "application/octet-stream"],
  [".json", "application/json"],
  [".css", "text/css"],
  [".png", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".svg", "image/svg+xml"],
]);

function resolveRequestPath(requestUrl) {
  const rawPath = decodeURIComponent((requestUrl || "/").split("?")[0]);
  const normalizedPath = path
    .normalize(rawPath === "/" ? "/index.html" : rawPath)
    .replace(/^[/\\]+/, "");
  const filePath = path.join(root, normalizedPath);
  return filePath.startsWith(root) ? filePath : null;
}

function getResponseHeaders(filePath) {
  let contentPath = filePath;
  let extension = path.extname(filePath).toLowerCase();
  const headers = {
    "Access-Control-Allow-Origin": "*",
    "Cache-Control": "no-cache",
  };

  if (extension === ".br") {
    headers["Content-Encoding"] = "br";
    contentPath = filePath.slice(0, -3);
    extension = path.extname(contentPath).toLowerCase();
  } else if (extension === ".gz") {
    headers["Content-Encoding"] = "gzip";
    contentPath = filePath.slice(0, -3);
    extension = path.extname(contentPath).toLowerCase();
  }

  headers["Content-Type"] = contentTypes.get(extension) || "application/octet-stream";
  return headers;
}

const server = http.createServer((request, response) => {
  const filePath = resolveRequestPath(request.url);
  if (!filePath || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    response.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
    response.end("Not found");
    return;
  }

  response.writeHead(200, getResponseHeaders(filePath));
  if (request.method === "HEAD") {
    response.end();
    return;
  }

  fs.createReadStream(filePath).pipe(response);
});

server.listen(port, host, () => {
  console.log(`Serving Unity WebGL from ${root}`);
  console.log(`Open http://${host}:${port}/`);
});
