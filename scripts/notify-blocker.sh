#!/usr/bin/env bash
# Send a maintenance blocker email through SMTP.
# Settings come from a project-scoped env file, never from this repository's
# tracked files or the global dotfiles secrets.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mail_env="${SPYGLASS_MAINTENANCE_ENV:-$script_dir/../.env.maintenance}"
if [[ -f "$mail_env" ]]; then
	set -a
	# shellcheck disable=SC1090
	source "$mail_env"
	set +a
fi

subject="${1:-Spyglass maintenance blocked}"
body="${2:-The weekly maintenance run blocked. Check the run report.}"

: "${SPYGLASS_SMTP_HOST:?SPYGLASS_SMTP_HOST is not set}"
: "${SPYGLASS_SMTP_PORT:?SPYGLASS_SMTP_PORT is not set}"
: "${SPYGLASS_SMTP_USER:?SPYGLASS_SMTP_USER is not set}"
: "${SPYGLASS_SMTP_PASSWORD:?SPYGLASS_SMTP_PASSWORD is not set}"
: "${SPYGLASS_EMAIL_TO:?SPYGLASS_EMAIL_TO is not set}"

export SPYGLASS_EMAIL_FROM="${SPYGLASS_EMAIL_FROM:-$SPYGLASS_SMTP_USER}"
export NOTIFY_SUBJECT="$subject"
export NOTIFY_BODY="$body"

python3 - <<'PY'
import os
import smtplib
import ssl
from email.message import EmailMessage
from email.utils import formataddr

host = os.environ["SPYGLASS_SMTP_HOST"]
port = int(os.environ["SPYGLASS_SMTP_PORT"])
user = os.environ["SPYGLASS_SMTP_USER"]
password = os.environ["SPYGLASS_SMTP_PASSWORD"]
sender = os.environ["SPYGLASS_EMAIL_FROM"]
sender_name = os.environ.get("SPYGLASS_EMAIL_FROM_NAME", "Spyglass Maintenance")
sender_name = sender_name.replace("\n", " ").strip()
to_header = os.environ["SPYGLASS_EMAIL_TO"]
recipients = [addr.strip() for addr in to_header.split(",") if addr.strip()]
subject = os.environ["NOTIFY_SUBJECT"].replace("\n", " ").strip()
body = os.environ["NOTIFY_BODY"]

if not recipients:
    raise SystemExit("SPYGLASS_EMAIL_TO contains no recipients")

message = EmailMessage()
message["Subject"] = subject
message["From"] = formataddr((sender_name, sender))
message["To"] = ", ".join(recipients)
message.set_content(body)

context = ssl.create_default_context()
try:
    if port == 465:
        with smtplib.SMTP_SSL(host, port, context=context, timeout=30) as smtp:
            smtp.login(user, password)
            smtp.send_message(message, from_addr=sender, to_addrs=recipients)
    else:
        with smtplib.SMTP(host, port, timeout=30) as smtp:
            smtp.ehlo()
            smtp.starttls(context=context)
            smtp.ehlo()
            smtp.login(user, password)
            smtp.send_message(message, from_addr=sender, to_addrs=recipients)
except Exception as exc:
    raise SystemExit(f"email notification failed: {type(exc).__name__}: {exc}") from exc

print(f"email notification sent to {', '.join(recipients)}")
PY
