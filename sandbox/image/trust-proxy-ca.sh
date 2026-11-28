#!/usr/bin/env bash
# Makes Chromium (Playwright) trust the sbx proxy's CA. sbx installs that CA into the system store when it starts the
# sandbox, so curl, Node and .NET trust it, but Chromium on Linux reads trust only from the user's NSS database. The CA
# is not known at build time, so a SessionStart hook (managed-settings.json) runs this on every session.
# Silent and never failing: a SessionStart hook's output goes into the session, and a sandbox without the proxy or
# certutil just keeps the old behaviour.
set -uo pipefail

ca=/usr/local/share/ca-certificates/proxy-ca.crt
db=sql:${HOME}/.pki/nssdb
name=sbx-proxy-ca

[ -r "$ca" ] && command -v certutil >/dev/null 2>&1 || exit 0

mkdir -p "${HOME}/.pki/nssdb"
certutil -d "$db" -L >/dev/null 2>&1 || certutil -d "$db" -N --empty-password >/dev/null 2>&1 || exit 0

# A recreated sandbox may bring a new CA: replace the old one instead of adding a second with the same name
certutil -d "$db" -D -n "$name" >/dev/null 2>&1
certutil -d "$db" -A -t "C,," -n "$name" -i "$ca" >/dev/null 2>&1
exit 0
