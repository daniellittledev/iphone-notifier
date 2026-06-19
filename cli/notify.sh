#!/usr/bin/env bash
# Send a push notification to your iPhone via the relay.
#
# Config (env vars):
#   NTFY_API_URL   e.g. https://ntfy-func-abcde.azurewebsites.net/api
#   NTFY_API_KEY   a CLI token created in the app (ntfy_...)
#
# Usage:
#   notify.sh "Title" "Body text"
#   notify.sh -p high -d '{"url":"https://x"}' "Build done" "Deploy succeeded"
#   echo "piped body" | notify.sh "Title"
set -euo pipefail

PRIORITY="normal"
DATA=""
IDEMPOTENCY=""

usage() {
  echo "Usage: $0 [-p normal|high] [-d '<json>'] [-k idempotency-key] <title> [body]" >&2
  exit 1
}

while getopts ":p:d:k:h" opt; do
  case "$opt" in
    p) PRIORITY="$OPTARG" ;;
    d) DATA="$OPTARG" ;;
    k) IDEMPOTENCY="$OPTARG" ;;
    h) usage ;;
    *) usage ;;
  esac
done
shift $((OPTIND - 1))

[ $# -ge 1 ] || usage
: "${NTFY_API_URL:?set NTFY_API_URL}"
: "${NTFY_API_KEY:?set NTFY_API_KEY}"

TITLE="$1"
if [ $# -ge 2 ]; then
  BODY="$2"
elif [ ! -t 0 ]; then
  BODY="$(cat)"   # read body from stdin if piped
else
  BODY=""
fi

# Build JSON safely with python3 (handles escaping of arbitrary text).
PAYLOAD=$(TITLE="$TITLE" BODY="$BODY" PRIORITY="$PRIORITY" DATA="$DATA" IDEMPOTENCY="$IDEMPOTENCY" python3 - <<'PY'
import json, os
obj = {"title": os.environ["TITLE"], "body": os.environ["BODY"], "priority": os.environ["PRIORITY"]}
data = os.environ.get("DATA", "")
if data:
    obj["data"] = json.loads(data)
idem = os.environ.get("IDEMPOTENCY", "")
if idem:
    obj["idempotencyKey"] = idem
print(json.dumps(obj))
PY
)

curl -fsS -X POST "$NTFY_API_URL/notifications/send" \
  -H "x-api-key: $NTFY_API_KEY" \
  -H "Content-Type: application/json" \
  -d "$PAYLOAD"
echo
