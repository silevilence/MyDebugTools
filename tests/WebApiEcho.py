"""Loopback fixture for WebApi.ps1. No third-party dependencies."""
import json
import sys
import time
from email import policy
from email.parser import BytesParser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlsplit


class Echo(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def handle_request(self):
        url = urlsplit(self.path)
        if url.path.startswith("/probe"):
            Path(sys.argv[2]).touch()
        if url.path in ("/slow", "/shutdown"):
            if url.path == "/shutdown":
                Path(sys.argv[3]).touch()
            time.sleep(3)
        media = self.headers.get("Content-Type", "")
        data = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        fields = []
        if media.startswith("multipart/"):
            message = BytesParser(policy=policy.default).parsebytes(
                f"Content-Type: {media}\r\n\r\n".encode() + data)
            for part in message.iter_parts():
                fields.append([part.get_param("name", header="content-disposition"),
                               part.get_payload(decode=True).hex()])
        echo = dict(method=self.command, path=unquote(url.path), query=parse_qs(url.query),
                    header=self.headers.get("X-Test"), media=media, data=data.hex(), fields=fields)
        routes = {
            "/xml": (200, "application/xml", "<root><value>42</value></root>"),
            "/broken": (200, "application/json", '{"值":?}'),
            "/html": (503, "text/html", '<h1>服务维护中</h1><p>稍后重试</p>'
                      '<script>fetch("/probe-script")</script><img src="/probe-image">'
                      '<link rel="stylesheet" href="/probe-style"><iframe src="/probe-frame"></iframe>'),
        }
        code, mime, body = routes.get(url.path, (200, "application/json", json.dumps(echo, ensure_ascii=False)))
        result = body.encode()
        self.send_response(code)
        self.send_header("Content-Type", mime + "; charset=utf-8")
        self.send_header("Content-Length", str(len(result)))
        self.send_header("X-Echo", "local")
        self.end_headers()
        try:
            self.wfile.write(result)
        except (ConnectionError, OSError):
            pass

    do_GET = do_POST = do_CHECK = handle_request


server = ThreadingHTTPServer(("127.0.0.1", 0), Echo)
Path(sys.argv[1]).write_text(str(server.server_port))
server.serve_forever()
