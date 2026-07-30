import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(fileURLToPath(new URL(".", import.meta.url)));
const rootPrefix = `${root}${sep}`;
const portArg = process.argv.find(argument => argument.startsWith("--port="));
const port = Number(portArg?.slice("--port=".length) ?? 8088);

if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error("Pass a valid port with --port=<1-65535>.");
}

const contentTypes = new Map([
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".mjs", "text/javascript; charset=utf-8"],
  [".css", "text/css; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".svg", "image/svg+xml"]
]);

function send(response, status, body, contentType = "text/plain; charset=utf-8") {
  response.writeHead(status, { "content-type": contentType, "cache-control": "no-store" });
  response.end(body);
}

createServer(async (request, response) => {
  if (request.method !== "GET" && request.method !== "HEAD") {
    response.setHeader("allow", "GET, HEAD");
    response.end("Method not allowed");
    return;
  }

  const pathname = decodeURIComponent(new URL(request.url ?? "/", "http://localhost").pathname);
  const relativePath = pathname === "/" ? "demo.html" : pathname.slice(1);
  const filePath = resolve(root, relativePath);

  if (filePath !== root && !filePath.startsWith(rootPrefix)) {
    send(response, 403, "Forbidden");
    return;
  }

  try {
    if (!(await stat(filePath)).isFile()) {
      send(response, 404, "Not found");
      return;
    }

    const contentType = contentTypes.get(extname(filePath)) ?? "application/octet-stream";
    if (request.method === "HEAD") {
      response.writeHead(200, { "content-type": contentType, "cache-control": "no-store" });
      response.end();
      return;
    }

    send(response, 200, await readFile(filePath), contentType);
  } catch {
    send(response, 404, "Not found");
  }
}).listen(port, "127.0.0.1", () => {
  console.log(`Ghostagram harness: http://127.0.0.1:${port}/`);
});
