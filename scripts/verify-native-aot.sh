#!/usr/bin/env bash
# Runs a native AOT binary against a stubbed SDK server and checks it evaluated real config state
# and reported telemetry.
#
# The trim and AOT analyzers run on every build, but they only prove the SDK compiles clean. They
# cannot show that the published binary still works: metadata dropped by the trimmer fails at
# runtime, not at build time. That is what this checks.
#
#   scripts/verify-native-aot.sh path/to/published/binary
set -euo pipefail
BIN="$1"

cat > /tmp/cd-bundle.json <<'JSON'
{
  "kind": "full",
  "environmentId": "environment-1",
  "projectId": "project-1",
  "timestamp": "2026-08-01T12:00:00.000Z",
  "configs": {
    "temporary-feature-flag": {
      "id": "11111111-1111-4111-8111-111111111111",
      "key": "temporary-feature-flag",
      "type": "boolean",
      "target": {
        "defaultValue": false,
        "defaultValueId": "off",
        "rules": [{
          "id": "paid-plans", "type": "conditional", "order": 1,
          "value": true, "valueId": "on",
          "conditions": [{
            "id": "plan", "attribute": "traits", "trait": "/plan",
            "operator": "is one of", "targetType": "text", "targetValues": ["pro"]
          }]
        }]
      }
    }
  }
}
JSON

cat > /tmp/cd-stub.py <<'PY'
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer
from socketserver import ThreadingMixIn

BUNDLE = open("/tmp/cd-bundle.json", "rb").read()

class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_POST(self):
        self.rfile.read(int(self.headers.get("Content-Length") or 0))
        if "polling" in self.path:
            body, status = BUNDLE, 200
        else:
            open("/tmp/cd-telemetry.json", "w").write(self.path)
            body, status = b"{}", 202
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *a):
        pass

class S(ThreadingMixIn, HTTPServer):
    daemon_threads = True

S(("127.0.0.1", 5400), H).serve_forever()
PY

rm -f /tmp/cd-telemetry.json
python3 /tmp/cd-stub.py & STUB=$!
trap 'kill $STUB 2>/dev/null || true' EXIT
for _ in $(seq 1 30); do
  if python3 -c "import socket,sys; s=socket.socket(); sys.exit(s.connect_ex(('127.0.0.1',5400)))" 2>/dev/null; then break; fi
done

OUT=$(ConfigDirector__Url=http://127.0.0.1:5400/ "$BIN")
echo "$OUT"

fail=0
check() {
  if printf '%s' "$OUT" | grep -qx "$1"; then echo "  ok   $1"; else echo "  FAIL expected line: $1"; fail=1; fi
}
echo "--- assertions"
check "ready=True"
check "temporary-feature-flag=True"
check "configs=1"

if [ -f /tmp/cd-telemetry.json ]; then
  echo "  ok   telemetry reported"
else
  echo "  FAIL no telemetry reached the server"; fail=1
fi

[ "$fail" -eq 0 ] && echo "native AOT verification passed" || { echo "native AOT verification failed"; exit 1; }
