#!/usr/bin/env python3
"""SARVAGHNA — email delivery for update/digest reports.

Reads a JSON payload ({"subject", "bodyText", "bodyHtml"}) from a file path
argument (or stdin if no path given), and sends it via Gmail SMTP using an
App Password. Credentials come from environment variables only — never
hardcoded, never logged.

Required env vars:
  GMAIL_ADDRESS       the sending Gmail account
  GMAIL_APP_PASSWORD  a 16-char Gmail App Password for that account

Optional:
  REPORT_RECIPIENT    defaults to bharadwaj041089@gmail.com

If credentials are missing, this exits 0 with a warning rather than failing
the calling CI job — reporting is a nice-to-have, not a release gate.
"""
import json
import os
import smtplib
import sys
from email.mime.multipart import MIMEMultipart
from email.mime.text import MIMEText

DEFAULT_RECIPIENT = "bharadwaj041089@gmail.com"


def load_payload():
    if len(sys.argv) > 1:
        with open(sys.argv[1], "r", encoding="utf-8") as f:
            return json.load(f)
    return json.load(sys.stdin)


def main():
    gmail_address = os.environ.get("GMAIL_ADDRESS")
    gmail_app_password = os.environ.get("GMAIL_APP_PASSWORD")
    recipient = os.environ.get("REPORT_RECIPIENT", DEFAULT_RECIPIENT)

    if not gmail_address or not gmail_app_password:
        print(
            "[sarvaghna/send_email] GMAIL_ADDRESS / GMAIL_APP_PASSWORD not set — "
            "skipping email delivery (report was generated but not sent).",
            file=sys.stderr,
        )
        return 0

    payload = load_payload()
    subject = payload.get("subject", "[Yukti] SARVAGHNA report")
    body_text = payload.get("bodyText", "")
    body_html = payload.get("bodyHtml")

    msg = MIMEMultipart("alternative")
    msg["Subject"] = subject
    msg["From"] = gmail_address
    msg["To"] = recipient
    msg.attach(MIMEText(body_text, "plain"))
    if body_html:
        msg.attach(MIMEText(body_html, "html"))

    with smtplib.SMTP("smtp.gmail.com", 587, timeout=20) as server:
        server.starttls()
        server.login(gmail_address, gmail_app_password)
        server.sendmail(gmail_address, [recipient], msg.as_string())

    print(f"[sarvaghna/send_email] Report sent to {recipient}: {subject}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
