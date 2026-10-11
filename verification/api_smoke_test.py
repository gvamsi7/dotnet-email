"""Exercise the published API with real HTTP, MIME files, and optionally TLS SMTP.

Uses only Python's standard library. --smtp requires Linux and OpenSSL, and trusts
an ephemeral test CA only in the child API process via SSL_CERT_FILE.
"""
import argparse
import base64
import concurrent.futures
import email
import email.policy
import http.client
import json
import os
from pathlib import Path
import socket
import socketserver
import ssl
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
from urllib.parse import urlsplit

KEY = "local-development-key-change-me"
PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZp0AAAAASUVORK5CYII=")
HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def free_port():
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def feedback(user="alice", **changes):
    value = dict(type="ReportABug", comments=f"{user}-only feedback", submittedBy=f"{user}@example.test", hardwareId=f"{user}-device")
    value.update(changes)
    return value


def check(condition, description):
    if not condition:
        raise AssertionError(description)
    print(f"PASS: {description}", flush=True)


class ApiHost:
    def __init__(self, args, root, **overrides):
        self.args, self.root, self.overrides = args, Path(root), overrides
        self.url = f"http://127.0.0.1:{free_port()}"
        self.mail = self.root / "mail"

    def __enter__(self):
        self.root.mkdir(parents=True)
        self.log = (self.root / "host.log").open("w+")
        env = os.environ.copy()
        # Fixture settings must be isolated from any real provider credentials.
        for name in list(env):
            if name.startswith(("FeedbackEmail__", "ApiSecurity__")):
                del env[name]
        env.update(ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development",
                   ASPNETCORE_URLS=self.url, FeedbackEmail__DeliveryMode="Pickup",
                   FeedbackEmail__PickupDirectory=str(self.mail), ApiSecurity__ApiKey=KEY,
                   Logging__LogLevel__Default="Warning")
        env.update(self.overrides)
        self.process = subprocess.Popen([self.args.dotnet, str(self.args.app)], cwd=self.args.app.parent,
                                        env=env, stdout=self.log, stderr=subprocess.STDOUT)
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                self.log.seek(0)
                details = self.log.read()[-3000:]
                self.__exit__(None, None, None)
                raise RuntimeError("API exited during startup:\n" + details)
            try:
                if self.request("GET", "/health")[0] == 200:
                    return self
            except (urllib.error.URLError, TimeoutError, ConnectionError):
                pass
            time.sleep(0.1)
        self.__exit__(None, None, None)
        raise TimeoutError("API failed to become healthy")

    def __exit__(self, *_):
        if self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait()
        self.log.close()

    def request(self, method, path, payload=None, *, key=KEY, content_type="application/json", raw=None):
        headers = {"Content-Type": content_type}
        if key is not None:
            headers["X-API-Key"] = key
        body = raw if raw is not None else (json.dumps(payload).encode() if payload is not None else None)
        request = urllib.request.Request(self.url + path, data=body, headers=headers, method=method)
        try:
            response = HTTP.open(request, timeout=15)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            return response.code, response.read(), response.headers

    def messages(self):
        return [email.message_from_bytes(file.read_bytes(), policy=email.policy.default)
                for file in self.mail.glob("*.eml")]

    def oversized_request_status(self):
        # Kestrel rejects the declared size before receiving a body. Sending the
        # entire body would let an HTTP client observe a broken pipe instead.
        url = urlsplit(self.url)
        connection = http.client.HTTPConnection(url.hostname, url.port, timeout=10)
        try:
            connection.request("POST", "/api/feedback", headers={
                "X-API-Key": KEY, "Content-Type": "application/json",
                "Content-Length": str(17 * 1024 * 1024)
            })
            return connection.getresponse().status
        finally:
            connection.close()


def pickup_checks(args, root):
    with ApiHost(args, root / "pickup") as host:
        check(host.request("GET", "/swagger/index.html")[0] == 200, "Swagger UI loads")
        code, data, _ = host.request("GET", "/swagger/v1/swagger.json")
        document = json.loads(data)
        check(code == 200 and "/api/feedback/upload" in document["paths"] and
              "ApiKey" in document["components"]["securitySchemes"], "OpenAPI describes uploads and API key authentication")
        for key in (None, "wrong-key"):
            check(host.request("POST", "/api/feedback", feedback(), key=key)[0] == 401, "Unauthenticated submission is rejected")

        comments = "<script>alert('x')</script> & {{Year}}\nSecond line"
        code, body, headers = host.request("POST", "/api/feedback/preview", feedback(comments=comments))
        html = body.decode()
        check(code == 200 and "&lt;script&gt;" in html and "<script>" not in html and
              "{{Year}}<br />Second line" in html and "UTC+05:30" in html, "Preview escapes HTML, preserves newlines, and renders India time")
        check(headers["Cache-Control"] == "no-store" and not host.messages(), "Preview does not save or send an email")

        attachment = dict(fileName="details.txt", content=base64.b64encode(b"Alice attachment bytes").decode())
        code, body, _ = host.request("POST", "/api/feedback", feedback(attachments=[attachment]))
        result = json.loads(body)
        check(code == 200 and result["status"] == "savedLocally" and result["messageId"], "JSON submission saves a MIME message and reports local delivery")
        check(host.request("POST", "/api/feedback", feedback("bob", type="GeneralFeedback"))[0] == 200, "A second user can submit feedback")
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            responses = list(executor.map(lambda user: host.request("POST", "/api/feedback", feedback(user)), ["carol", "dan"]))
        check(all(response[0] == 200 for response in responses), "Concurrent users can submit separate messages")

        boundary = "feedback-test-boundary"
        fields = feedback("upload")
        parts = []
        for name, value in fields.items():
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
        parts += [f'--{boundary}\r\nContent-Disposition: form-data; name="attachments"; filename="Screenshot1.png"\r\nContent-Type: image/png\r\n\r\n'.encode(),
                  PNG, f"\r\n--{boundary}--\r\n".encode()]
        check(host.request("POST", "/api/feedback/upload", raw=b"".join(parts),
                           content_type=f"multipart/form-data; boundary={boundary}")[0] == 200, "Multipart screenshot submission succeeds")
        messages = host.messages()
        check(len(messages) == 5 and len({message["Message-ID"] for message in messages}) == 5, "Five users produce five distinct messages")
        for user in ("alice", "bob", "carol", "dan", "upload"):
            message = next(m for m in messages if f"{user}@example.test" in m.get_body(("html",)).get_content())
            content = message.get_body(("html",)).get_content()
            check(f"{user}-only feedback" in content and all(f"{other}-only feedback" not in content
                  for other in ("alice", "bob", "carol", "dan", "upload") if other != user), f"{user}'s feedback stays isolated")
            check(message.get_body(("plain",)) is not None, "MIME message has a plain-text alternative")
            files = list(message.iter_attachments())
            if user in ("alice", "upload"):
                expected = b"Alice attachment bytes" if user == "alice" else PNG
                check(len(files) == 1 and files[0].get_payload(decode=True) == expected, "MIME attachment bytes are preserved")
            else:
                check(not files, "Attachments do not leak to another user")

    with ApiHost(args, root / "invalid") as host:
        invalid = [feedback(comments=" "), feedback(submittedBy="invalid-email"), feedback(type="Unknown"),
                   feedback(type=1), {k: v for k, v in feedback().items() if k != "type"},
                   feedback(attachments=[dict(fileName="details.txt", content="not-base64!")]),
                   feedback(attachments=[dict(fileName="../secret.txt", content="YQ==")]),
                   feedback(attachments=None), feedback(attachments=[None]),
                   feedback(attachments=[dict(fileName=f"{i}.txt", content="YQ==") for i in range(6)])]
        for payload in invalid:
            check(host.request("POST", "/api/feedback", payload)[0] == 400, "Invalid feedback returns HTTP 400")
        check(host.request("POST", "/api/feedback", feedback())[0] == 429, "Eleventh request in a minute is rate limited")
        check(not host.messages(), "Invalid and rate-limited requests produce no email")

    with ApiHost(args, root / "limits") as host:
        files = [dict(fileName="large.txt", content=base64.b64encode(b"x" * (6 * 1024 * 1024)).decode()),
                 dict(fileName="other.txt", content=base64.b64encode(b"x" * (5 * 1024 * 1024)).decode())]
        check(host.request("POST", "/api/feedback", feedback(attachments=files))[0] == 400, "Combined attachment size over 10 MiB is rejected")
        check(host.oversized_request_status() == 413, "HTTP request bodies over 16 MiB are rejected")
        check(not host.messages(), "Oversized requests produce no email")

    cases = [dict(ApiSecurity__ApiKey=KEY, FeedbackEmail__DeliveryMode="Smtp"),
             dict(ApiSecurity__ApiKey="k" * 40, FeedbackEmail__DeliveryMode="Pickup"),
             dict(ApiSecurity__ApiKey="k" * 40, FeedbackEmail__DeliveryMode="Smtp",
                  FeedbackEmail__Host="localhost", FeedbackEmail__Username="test-smtp-user",
                  FeedbackEmail__Password="")]
    for index, settings in enumerate(cases):
        env = os.environ.copy()
        for name in list(env):
            if name.startswith(("FeedbackEmail__", "ApiSecurity__")):
                del env[name]
        env.update(ASPNETCORE_ENVIRONMENT="Production", DOTNET_ENVIRONMENT="Production",
                   ASPNETCORE_URLS=f"http://127.0.0.1:{free_port()}")
        env.update(settings)
        process = subprocess.run([args.dotnet, str(args.app)], cwd=args.app.parent, env=env,
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=15)
        check(process.returncode != 0 and b"OptionsValidationException" in process.stdout,
              "Production rejects the development key, pickup delivery, or missing SMTP credentials")


def test_ca(root):
    root.mkdir()
    def run(*command):
        subprocess.run(command, cwd=root, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    # A trusted self-signed localhost certificate needs no external revocation
    # service. Normal TLS chain and hostname validation remain enabled.
    run("openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", "ca.key", "-out", "ca.crt",
        "-days", "1", "-subj", "/CN=localhost", "-addext", "basicConstraints=critical,CA:TRUE",
        "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1", "-addext", "extendedKeyUsage=serverAuth")
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(root / "ca.crt", root / "ca.key")
    return context


class SmtpHandler(socketserver.StreamRequestHandler):
    def handle(self):
        server = self.server
        if server.implicit_tls:
            self.connection = server.context.wrap_socket(self.connection, server_side=True)
            self.rfile = self.connection.makefile("rb")
            self.wfile = self.connection.makefile("wb")
        encrypted = server.implicit_tls
        authenticated = False
        def reply(value):
            self.wfile.write((value + "\r\n").encode())
            self.wfile.flush()
        reply("220 localhost test SMTP")
        try:
            while True:
                line = self.rfile.readline()
                if not line:
                    break
                command = line.decode().strip()
                verb = command.split(" ", 1)[0].upper()
                if verb in ("EHLO", "HELO"):
                    reply("250-localhost\r\n" + ("250-AUTH PLAIN\r\n" if encrypted else "250-STARTTLS\r\n") + "250 SIZE 25000000")
                elif verb == "STARTTLS":
                    reply("220 Begin TLS")
                    self.connection = server.context.wrap_socket(self.connection, server_side=True)
                    self.rfile = self.connection.makefile("rb")
                    self.wfile = self.connection.makefile("wb")
                    encrypted = True
                elif verb == "AUTH":
                    token = command.split(" ")[-1]
                    if token.upper() == "PLAIN":
                        reply("334 ")
                        token = self.rfile.readline().strip()
                    authenticated = base64.b64decode(token) == b"\0test-smtp-user\0smtp-test-password"
                    reply("235 Authentication succeeded" if authenticated else "535 Authentication failed")
                elif verb == "MAIL":
                    reply("250 Sender accepted" if authenticated else "530 Authentication required")
                elif verb == "RCPT":
                    server.recipients.append(command)
                    reply("250 Recipient accepted")
                elif verb == "DATA":
                    reply("354 End with a dot")
                    data = bytearray()
                    while True:
                        line = self.rfile.readline()
                        if not line or line == b".\r\n":
                            break
                        data.extend(line[1:] if line.startswith(b"..") else line)
                    server.messages.append(bytes(data))
                    reply("250 Message accepted")
                elif verb == "QUIT":
                    reply("221 Bye")
                    break
                else:
                    reply("250 OK")
        except (ssl.SSLError, ConnectionError, OSError) as error:
            server.errors.append(type(error).__name__ + ": " + str(error))


class SmtpServer(socketserver.ThreadingTCPServer):
    daemon_threads = True
    allow_reuse_address = True
    def __init__(self, context, implicit_tls=False):
        super().__init__(("127.0.0.1", 0), SmtpHandler)
        self.context, self.implicit_tls = context, implicit_tls
        self.messages, self.recipients = [], []
        self.errors = []


def smtp_checks(args, root):
    context = test_ca(root / "certificates")
    for implicit in (False, True):
        with SmtpServer(context, implicit_tls=implicit) as server:
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            config = dict(FeedbackEmail__DeliveryMode="Smtp", FeedbackEmail__Host="localhost",
                          FeedbackEmail__Port=str(server.server_address[1]),
                          FeedbackEmail__Security="SslOnConnect" if implicit else "StartTls",
                          FeedbackEmail__Authentication="Password", FeedbackEmail__Username="test-smtp-user",
                          FeedbackEmail__Password="smtp-test-password", FeedbackEmail__FromAddress="sender@example.test",
                          FeedbackEmail__ToAddress="support@example.test", FeedbackEmail__TimeoutSeconds="5",
                          SSL_CERT_FILE=str(root / "certificates/ca.crt"))
            with ApiHost(args, root / f"smtp-{implicit}", **config) as host:
                payload = feedback("smtp", attachments=[dict(fileName="Screenshot1.png", content=base64.b64encode(PNG).decode())])
                code, body, _ = host.request("POST", "/api/feedback", payload)
                if code != 200:
                    host.log.flush()
                    host.log.seek(0)
                    raise AssertionError(f"SMTP fixture failed: HTTP {code}; {body.decode()}; "
                                         f"{host.log.read()}; TLS fixture diagnostics: {server.errors}")
                check(code == 200 and json.loads(body)["status"] == "smtpAccepted", "Authenticated TLS SMTP accepts the message: " + config["FeedbackEmail__Security"])
                check(len(server.messages) == 1 and not host.messages(), "SMTP mode sends one real MIME message without creating a pickup file")
                message = email.message_from_bytes(server.messages[0], policy=email.policy.default)
                check(message["From"].addresses[0].addr_spec == "sender@example.test" and
                      message["To"].addresses[0].addr_spec == "support@example.test" and
                      all("support@example.test" in recipient for recipient in server.recipients), "Sender and recipient come from server configuration")
                check(list(message.iter_attachments())[0].get_payload(decode=True) == PNG and
                      "smtp@example.test" in message.get_body(("html",)).get_content(), "TLS SMTP preserves feedback and screenshot content")
            if not implicit:
                config["FeedbackEmail__Password"] = "incorrect-test-password"
                with ApiHost(args, root / "smtp-auth-failure", **config) as host:
                    code, body, _ = host.request("POST", "/api/feedback", feedback())
                    check(code == 502 and b"incorrect-test-password" not in body, "SMTP authentication failure returns a safe HTTP 502")
                check(len(server.messages) == 1, "Failed SMTP authentication does not send a message")
                config["FeedbackEmail__Password"] = "smtp-test-password"
                empty_trust = root / "empty-trust.pem"
                empty_trust.write_text("")
                config["SSL_CERT_FILE"] = str(empty_trust)
                with ApiHost(args, root / "smtp-untrusted-certificate", **config) as host:
                    code, _, _ = host.request("POST", "/api/feedback", feedback())
                    check(code == 502, "An untrusted SMTP certificate is rejected")
                check(len(server.messages) == 1, "TLS validation failure does not send a message")
            server.shutdown()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", type=Path, required=True, help="Published API DLL")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--smtp", action="store_true")
    args = parser.parse_args()
    args.app = args.app.resolve()
    with tempfile.TemporaryDirectory(prefix="feedback-api-tests-") as directory:
        root = Path(directory)
        pickup_checks(args, root)
        if args.smtp:
            smtp_checks(args, root)
    print("PASS: API verification completed.", flush=True)


if __name__ == "__main__":
    main()
