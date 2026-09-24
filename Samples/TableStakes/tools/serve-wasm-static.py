#!/usr/bin/env python3
"""Serve a published Blazor WebAssembly client the way a deployed server serves it.

`python3 -m http.server` is not enough for a published client. It does not send the `.wasm` MIME type, so the
browser refuses to load the module. It ignores the `.br` and `.gz` files that `dotnet publish` writes next to
each asset, so the browser downloads the uncompressed files. It returns 404 for a client-side route instead of
serving `index.html`.

`tools/run-e2e.py --trimmed` uses this server to test the trimmed client. Only `dotnet publish` trims, so the
dev server (`dotnet run`) always serves an untrimmed build, and a type that trimming removed is missing only in
the published output.

Uses only the Python standard library.

Usage:
  serve-wasm-static.py <wwwroot> <port> [--bind ADDR]

  <wwwroot>    the published client's wwwroot, e.g. WebClient/bin/Release/net10.0-browser/publish/wwwroot
  <port>       TCP port to listen on
  --bind ADDR  interface to bind (default 0.0.0.0, matching the dev server the harness otherwise starts)
"""
import http.server
import os
import posixpath
import shutil
import socketserver
import sys
import urllib.parse


# Content types for the file types in a published client. Other files, including Blazor's `.dat` ICU data and
# the game's `.mpa` config archive, are served as application/octet-stream. `.wasm` must be application/wasm,
# or the browser refuses to compile it and the client does not start.
CONTENT_TYPES = {
    ".wasm":        "application/wasm",
    ".js":          "text/javascript",
    ".mjs":         "text/javascript",
    ".json":        "application/json",
    ".css":         "text/css",
    ".html":        "text/html; charset=utf-8",
    ".htm":         "text/html; charset=utf-8",
    ".png":         "image/png",
    ".jpg":         "image/jpeg",
    ".jpeg":        "image/jpeg",
    ".svg":         "image/svg+xml",
    ".ico":         "image/x-icon",
    ".woff2":       "font/woff2",
    ".webmanifest": "application/manifest+json",
    ".txt":         "text/plain; charset=utf-8",
}

# In order of preference. Brotli files are smaller than gzip files for the framework assemblies.
ENCODINGS = (("br", ".br"), ("gzip", ".gz"))


def accepted_encodings(header):
    # Returns the set of encodings the client accepts. The client's q-value ranking is ignored because the
    # server picks by ENCODINGS order. An encoding with q=0 (including `q=0.000`) is refused and left out.
    offered = set()
    for part in (header or "").split(","):
        token = part.strip().lower()
        if not token:
            continue
        name, _, params = token.partition(";")
        if _weight(params) == 0.0:
            continue
        offered.add(name.strip())
    return offered


def _weight(params):
    """The q value in an Accept-Encoding parameter string, defaulting to 1.0 when there is none to read."""
    for parameter in params.split(";"):
        key, _, value = parameter.partition("=")
        if key.strip() == "q":
            try:
                return float(value.strip())
            except ValueError:
                return 1.0
    return 1.0


class Handler(http.server.BaseHTTPRequestHandler):
    # HTTP/1.1 keeps connections open between requests. A client start makes hundreds of requests, and a new
    # connection for each one would slow the start down.
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        pass  # The harness saves this process's output as the client log, and a line per request would hide the errors.

    def do_GET(self):
        self.serve(send_body=True)

    def do_HEAD(self):
        self.serve(send_body=False)

    def serve(self, send_body):
        path = self.resolve(urllib.parse.urlparse(self.path).path)
        if path is None:
            self.send_error(404)
            return

        encoding, disk_path = self.negotiate(path)
        try:
            f = open(disk_path, "rb")
        except OSError:
            self.send_error(404)
            return

        # The body is streamed from the open file instead of read into memory first, because the framework
        # files are megabytes each and the handler threads serve many of them in parallel.
        with f:
            content_type = CONTENT_TYPES.get(os.path.splitext(path)[1].lower(), "application/octet-stream")
            self.send_response(200)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(os.fstat(f.fileno()).st_size))
            if encoding:
                self.send_header("Content-Encoding", encoding)
            # no-cache makes the browser revalidate every asset, so that a test never runs a cached asset from an
            # earlier build, including when the stack is redeployed while a browser has the page open.
            self.send_header("Cache-Control", "no-cache")
            self.send_header("Vary", "Accept-Encoding")
            self.end_headers()
            if not send_body:
                return
            try:
                shutil.copyfileobj(f, self.wfile)
            except (BrokenPipeError, ConnectionResetError):
                # The browser closed the connection mid-response, for example on navigation or in a test that
                # aborts framework requests. Without this, socketserver would print a traceback into the client log.
                pass

    def resolve(self, url_path):
        """The file this request names, or None. Client-side routes fall back to index.html."""
        relative = posixpath.normpath(urllib.parse.unquote(url_path)).lstrip("/")
        if relative in ("", "."):
            relative = "index.html"

        candidate = os.path.normpath(os.path.join(self.server.wwwroot, relative))
        # Path traversal: a request may not leave the published root.
        if not candidate.startswith(self.server.wwwroot + os.sep) and candidate != self.server.wwwroot:
            return None
        if os.path.isfile(candidate):
            return relative

        # A missing path with a file extension is a missing file, so return 404. Serving index.html for a
        # missing .wasm or .js file would show up as a confusing MIME type error. A missing path without an
        # extension is a client-side route, so serve index.html.
        if os.path.splitext(relative)[1]:
            return None
        return "index.html"

    def negotiate(self, relative):
        """The pre-compressed sibling to serve, as (Content-Encoding, path), or (None, path) for the raw file."""
        raw_path = os.path.join(self.server.wwwroot, relative)
        offered = accepted_encodings(self.headers.get("Accept-Encoding"))
        for name, suffix in ENCODINGS:
            if name in offered and os.path.isfile(raw_path + suffix):
                return name, raw_path + suffix
        return None, raw_path


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main(argv):
    if "--help" in argv or "-h" in argv:
        print(__doc__)
        return 0

    bind_address = "0.0.0.0"
    if "--bind" in argv:
        index = argv.index("--bind")
        if index + 1 >= len(argv):
            sys.stderr.write("serve-wasm-static.py: --bind needs an address\n")
            return 2
        bind_address = argv[index + 1]
        del argv[index:index + 2]

    if len(argv) != 2:
        sys.stderr.write(__doc__)
        return 2

    wwwroot = os.path.abspath(argv[0])
    port = int(argv[1])
    if not os.path.isfile(os.path.join(wwwroot, "index.html")):
        sys.stderr.write("serve-wasm-static.py: %s holds no index.html — is it a *publish* wwwroot?\n" % wwwroot)
        return 2

    server = Server((bind_address, port), Handler)
    server.wwwroot = wwwroot
    print("serve-wasm-static.py: serving %s on http://%s:%d" % (wwwroot, bind_address, port), flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
