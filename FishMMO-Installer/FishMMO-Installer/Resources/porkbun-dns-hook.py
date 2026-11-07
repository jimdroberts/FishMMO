#!/usr/bin/env python3
"""Certbot --manual DNS-01 hook for domains whose DNS is hosted at Porkbun.

Certbot runs it as:
    porkbun-dns-hook.py auth      (--manual-auth-hook)
    porkbun-dns-hook.py cleanup   (--manual-cleanup-hook)

It also points the game's host names at this server (FishMMO-Installer: Web Server > Point DNS
records at this host), with the same credentials:
    porkbun-dns-hook.py records [--ip ADDRESS] [--dry-run] [NAME ...]
NAME is relative to the zone (default: api play game panel). Each gets one A record holding
ADDRESS, the public IPv4 address (default: the address Porkbun sees this host connect from).
Existing A records of that name are updated, a missing one is created, and a name that is a CNAME
is reported and left alone. Nothing else in the zone is touched. Rerun it whenever the ISP changes
the address; it changes nothing when the records are already right.
with CERTBOT_DOMAIN and CERTBOT_VALIDATION set, and CERTBOT_AUTH_OUTPUT (the auth step's stdout)
set for cleanup. The hook commands are saved in the certificate's renewal configuration, so
`certbot renew` repeats the challenge unattended.

It uses only the Python standard library (certbot already requires Python), so no certbot plugin
has to be packaged for the distribution. `dig`, when installed, is used to wait until every
authoritative Porkbun nameserver serves the record; without it the hook waits a fixed time.

Credentials file (root:root 0600), default /etc/letsencrypt/fishmmo-porkbun.ini, override with
FISHMMO_PORKBUN_CREDENTIALS:
    PORKBUN_API_KEY=pk1_...
    PORKBUN_SECRET_API_KEY=sk1_...
    PORKBUN_DOMAIN=example.com        # the zone registered at Porkbun

Porkbun side: create the key at https://porkbun.com/account/api with DNS permission, and turn on
"API Access" for the domain (Domain Management > the domain > Details). Without it every call
fails with API_ACCESS_DISABLED.

Optional environment: PORKBUN_PROPAGATION_TIMEOUT (seconds, default 600) and
PORKBUN_PROPAGATION_SECONDS (fixed wait without dig, default 120).
"""

import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

API = "https://api.porkbun.com/api/json/v3"
# Same API, reachable over IPv4 only, so 'ping' reports the public IPv4 address even on a
# dual-stack host.
API_IPV4 = "https://api-ipv4.porkbun.com/api/json/v3"
DEFAULT_RECORD_NAMES = ("api", "play", "game", "panel")
DEFAULT_CREDENTIALS = "/etc/letsencrypt/fishmmo-porkbun.ini"
TTL = 600  # Porkbun's usual account minimum; a lower value is raised to it anyway.


def log(message):
    # stdout is reserved for the record id that certbot hands to the cleanup step.
    print(f"porkbun-dns-hook: {message}", file=sys.stderr, flush=True)


def fail(message):
    log(message)
    sys.exit(1)


def load_credentials():
    path = os.environ.get("FISHMMO_PORKBUN_CREDENTIALS", DEFAULT_CREDENTIALS)
    values = {}
    try:
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip().strip('"').strip("'")
    except OSError as error:
        fail(f"cannot read credentials file {path}: {error}")
    missing = [k for k in ("PORKBUN_API_KEY", "PORKBUN_SECRET_API_KEY", "PORKBUN_DOMAIN") if not values.get(k)]
    if missing:
        fail(f"{path} is missing {', '.join(missing)}")
    values["PORKBUN_DOMAIN"] = values["PORKBUN_DOMAIN"].lower().rstrip(".")
    return values


def call(path, credentials, body=None, base=API):
    payload = {"apikey": credentials["PORKBUN_API_KEY"], "secretapikey": credentials["PORKBUN_SECRET_API_KEY"]}
    payload.update(body or {})
    data = json.dumps(payload).encode("utf-8")
    for attempt in range(6):
        request = urllib.request.Request(f"{base}/{path}", data=data, method="POST",
                                         headers={"Content-Type": "application/json",
                                                  "User-Agent": "certbot-dns-hook"})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                result = json.loads(response.read().decode("utf-8"))
        except urllib.error.HTTPError as error:
            if error.code == 429 and attempt < 5:
                wait = int(error.headers.get("Retry-After") or 5)
                log(f"rate limited; retrying in {wait}s")
                time.sleep(min(wait, 60))
                continue
            detail = error.read().decode("utf-8", "replace")
            fail(f"{path} failed: HTTP {error.code} {detail}")
        except (urllib.error.URLError, OSError, ValueError) as error:
            fail(f"{path} failed: {error}")
        if result.get("status") != "SUCCESS":
            fail(f"{path} refused: {result.get('code', '')} {result.get('message', result)}")
        return result
    fail(f"{path} failed: still rate limited")


def record_name(domain, zone):
    """Returns the record name relative to the zone, e.g. '_acme-challenge.api'."""
    domain = domain.lower().rstrip(".")
    if domain.startswith("*."):
        domain = domain[2:]
    if domain == zone:
        return "_acme-challenge"
    if domain.endswith("." + zone):
        return "_acme-challenge." + domain[: -len(zone) - 1]
    fail(f"{domain} is not inside the Porkbun zone {zone} (PORKBUN_DOMAIN)")


def dig(*args):
    try:
        out = subprocess.run(["dig", "+short", *args], capture_output=True, text=True, timeout=15)
    except (OSError, subprocess.TimeoutExpired):
        return []
    return [line.strip() for line in out.stdout.splitlines() if line.strip()]


def wait_for_propagation(fqdn, value, zone):
    if shutil.which("dig") is None:
        seconds = int(os.environ.get("PORKBUN_PROPAGATION_SECONDS", "120"))
        log(f"dig not installed; waiting a fixed {seconds}s for DNS propagation")
        time.sleep(seconds)
        return
    nameservers = [ns.rstrip(".") for ns in dig("NS", zone)]
    if not nameservers:
        log(f"could not look up the nameservers of {zone}; waiting 120s instead")
        time.sleep(120)
        return
    deadline = time.monotonic() + int(os.environ.get("PORKBUN_PROPAGATION_TIMEOUT", "600"))
    while True:
        pending = [ns for ns in nameservers
                   if value not in [t.strip('"') for t in dig("TXT", fqdn, "@" + ns)]]
        if not pending:
            log(f"{fqdn} is served by every nameserver ({', '.join(nameservers)})")
            # Let's Encrypt resolves through its own recursive resolvers; a short margin covers
            # a nameserver answering one moment before another of the same anycast set.
            time.sleep(10)
            return
        if time.monotonic() > deadline:
            fail(f"{fqdn} not visible on {', '.join(pending)} before the timeout")
        time.sleep(10)


def auth():
    domain = os.environ.get("CERTBOT_DOMAIN") or fail("CERTBOT_DOMAIN is not set")
    value = os.environ.get("CERTBOT_VALIDATION") or fail("CERTBOT_VALIDATION is not set")
    credentials = load_credentials()
    zone = credentials["PORKBUN_DOMAIN"]
    name = record_name(domain, zone)
    result = call(f"dns/create/{zone}", credentials,
                  {"name": name, "type": "TXT", "content": value, "ttl": TTL})
    record_id = result.get("id")
    if record_id is None:
        fail("Porkbun did not return a record id")
    log(f"created TXT {name}.{zone} (id {record_id})")
    print(record_id, flush=True)
    wait_for_propagation(f"{name}.{zone}", value, zone)


def cleanup():
    domain = os.environ.get("CERTBOT_DOMAIN", "")
    value = os.environ.get("CERTBOT_VALIDATION", "")
    credentials = load_credentials()
    zone = credentials["PORKBUN_DOMAIN"]
    lines = (os.environ.get("CERTBOT_AUTH_OUTPUT") or "").strip().splitlines()
    record_id = lines[-1].strip() if lines else ""
    try:
        if record_id.isdigit():
            call(f"dns/delete/{zone}/{record_id}", credentials)
            log(f"deleted TXT record {record_id}")
            return
        # No id from the auth step: find the record by name and value instead.
        name = record_name(domain, zone)
        records = call(f"dns/retrieveByNameType/{zone}/TXT/{name}", credentials).get("records") or []
        for record in records:
            if str(record.get("content", "")).strip('"') == value and record.get("id") is not None:
                call(f"dns/delete/{zone}/{record['id']}", credentials)
                log(f"deleted TXT record {record['id']}")
    except SystemExit:
        # A record left behind is harmless; never fail a certificate that was already issued.
        log("cleanup failed; delete the leftover _acme-challenge TXT record in Porkbun by hand")


def is_ipv4(value):
    parts = value.split(".")
    return len(parts) == 4 and all(p.isdigit() and 0 <= int(p) <= 255 and str(int(p)) == p for p in parts)


def is_record_name(name):
    labels = name.split(".")
    return all(label and len(label) <= 63 and label[0] != "-" and label[-1] != "-"
               and all(c.isalnum() or c == "-" for c in label) for label in labels)


def records(args):
    """Creates or updates one A record per name. Returns the process exit code."""
    address, dry_run, names = None, False, []
    iterator = iter(args)
    for arg in iterator:
        if arg == "--ip":
            address = next(iterator, None) or fail("--ip needs an address")
        elif arg == "--dry-run":
            dry_run = True
        elif arg.startswith("-"):
            fail(f"unknown option {arg}")
        else:
            names.append(arg.lower().rstrip("."))
    names = names or list(DEFAULT_RECORD_NAMES)

    credentials = load_credentials()
    zone = credentials["PORKBUN_DOMAIN"]
    for i, name in enumerate(names):
        if name.endswith("." + zone):
            names[i] = name = name[: -len(zone) - 1]
        if name in ("", "@", zone) or name.startswith("*") or not is_record_name(name):
            fail(f"'{name}' is not a host name inside {zone}; the apex and wildcards are left alone")

    if address is None:
        address = str(call("ping", credentials, base=API_IPV4).get("yourIp", ""))
        log(f"Porkbun sees this host connect from {address}")
    if not is_ipv4(address):
        fail(f"'{address}' is not a public IPv4 address; pass one with --ip")
    first = int(address.split(".")[0])
    second = int(address.split(".")[1])
    if first in (0, 10, 127) or (first == 100 and 64 <= second <= 127) or (first == 169 and second == 254) \
            or (first == 172 and 16 <= second <= 31) or (first == 192 and second == 168) or first >= 224:
        fail(f"{address} is a private, shared (CGNAT) or reserved address; players cannot reach it")

    failures = 0
    for name in names:
        fqdn = f"{name}.{zone}"
        existing = call(f"dns/retrieveByNameType/{zone}/A/{name}", credentials).get("records") or []
        contents = sorted(str(r.get("content", "")) for r in existing)
        if contents == [address]:
            print(f"{fqdn}: A {address} (already correct)", flush=True)
            continue
        if existing:
            action = f"A {', '.join(contents)} -> {address}"
            if not dry_run:
                call(f"dns/editByNameType/{zone}/A/{name}", credentials, {"content": address, "ttl": TTL})
        else:
            if call(f"dns/retrieveByNameType/{zone}/CNAME/{name}", credentials).get("records"):
                print(f"{fqdn}: is a CNAME; delete it in Porkbun first (left unchanged)", flush=True)
                failures += 1
                continue
            action = f"A {address} (new)"
            if not dry_run:
                call(f"dns/create/{zone}", credentials, {"name": name, "type": "A", "content": address, "ttl": TTL})
        print(f"{fqdn}: {action}{' [dry run, not changed]' if dry_run else ''}", flush=True)
    return 1 if failures else 0


def main():
    if len(sys.argv) >= 2 and sys.argv[1] == "records":
        sys.exit(records(sys.argv[2:]))
    if len(sys.argv) != 2 or sys.argv[1] not in ("auth", "cleanup"):
        print(__doc__, file=sys.stderr)
        sys.exit(2)
    auth() if sys.argv[1] == "auth" else cleanup()


if __name__ == "__main__":
    main()
