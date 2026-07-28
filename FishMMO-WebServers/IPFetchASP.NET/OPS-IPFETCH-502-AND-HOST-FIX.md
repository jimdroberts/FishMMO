# Ops fix: IPFetch 502 + host alignment (eqbrowser.com)

**Audience:** Unity Grok / server deploy agent  
**Symptom:** Client discovery fails; nginx returns **502** on:

- `https://api.eqbrowser.com/loginserver`
- `https://ipfetch.eqbrowser.com/loginserver`

**Meaning:** Nginx is fine; **IpFetchServer is not running** (or not on `127.0.0.1:8080`).

This doc is the playbook that fixed production on the eqbrowser droplet. Apply the same steps after fresh monorepo layout moves or clean GameServer / web publishes.

---

## 1. Root cause checklist (in order)

| Check | Failure mode | Fix |
|--------|----------------|-----|
| `systemctl status fishmmo-ipfetch` | `status=200/CHDIR` | Publish path missing; unit points at old `/var/www/FishMMO-WebServers/...` |
| `ss -tlnp \| grep 8080` | nothing listening | Service crash-looping; read journal |
| Journal: constructors ambiguous | DI `NpgsqlDbContextFactory` | Explicit factory registration (see §3) |
| Journal: ForwardedHeaders must be configured | Production hardening | `appsettings.Production.json` KnownProxies/Networks (see §4) |
| Journal: Gate secret not found | empty `deployment_secrets` | Installer “Configure Server Keys” / SQL seed |
| Local `/loginserver` → **401** | expected without client signature | Client must send `X-FishMMO-Client` |
| Local signed `/loginserver` → **404** No login servers | empty `login_servers` table | Game LoginServer must register, or seed row (see §6) |
| Signed → **200** `{"Ports":[7770],...}` | healthy | Client discovery should work |

---

## 2. Fix systemd path + publish IPFetch

Monorepo lives under **`/var/www/src/FishMMO`** (not `/var/www/FishMMO-WebServers`).

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

PUB=/var/www/src/FishMMO/FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/bin/Release/net8.0/publish
mkdir -p /var/log/fishmmo

cd /var/www/src/FishMMO/FishMMO-WebServers/IPFetchASP.NET
dotnet publish IpFetchServer/IpFetchServer.csproj -c Release -o "$PUB"

# Unit (paths MUST match publish)
sudo tee /etc/systemd/system/fishmmo-ipfetch.service >/dev/null <<'EOF'
[Unit]
Description=FishMMO IP Fetch Web Server
After=network.target postgresql.service

[Service]
WorkingDirectory=/var/www/src/FishMMO/FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/bin/Release/net8.0/publish
ExecStart=/usr/bin/dotnet /var/www/src/FishMMO/FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/bin/Release/net8.0/publish/IpFetchServer.dll
Restart=always
RestartSec=5
User=root
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=FISHMMO_ENVIRONMENT=Production
Environment=DOTNET_ROOT=/root/.dotnet
Environment=PATH=/root/.dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin
EnvironmentFile=-/var/www/src/FishMMO/FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/bin/Release/net8.0/publish/fishmmo-secrets.env
Environment=FISHMMO_PUBLIC_ADVERTISE_IP=161.35.58.193

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
```

### Secrets file next to the publish (`fishmmo-secrets.env`, chmod 600)

Do **not** commit this file. Values must match host DB + gate:

```bash
# Example shape only — use real host secrets
cat > "$PUB/fishmmo-secrets.env" <<'EOF'
FISHMMO_DB_HOST=127.0.0.1
FISHMMO_DB_PORT=5432
FISHMMO_DB_NAME=fish_mmo_postgresql
FISHMMO_DB_USERNAME=fishmmo
FISHMMO_DB_PASSWORD=<from /etc/fishmmo/db-secrets.env>
ConnectionStrings__NpgsqlConnection=Host=127.0.0.1;Port=5432;Database=fish_mmo_postgresql;Username=fishmmo;Password=<same>;Ssl Mode=Prefer;
ConnectionStrings__AllowInsecureNpgsql=true
FISHMMO_PUBLIC_ADVERTISE_IP=<public IPv4>
EOF
chmod 600 "$PUB/fishmmo-secrets.env"
```

**Notes:**
- Production refuses Npgsql without SSL unless `AllowInsecureNpgsql=true` (loopback OK).
- Gate secret is **only** from DB table `deployment_secrets` key `client_gate_secret` — not from env.
- Connection-token HMAC is **only** from `connection_token_keys` — not from appsettings.

---

## 3. Code fix: NpgsqlDbContextFactory DI ambiguity (must stay in source)

**Error:**

```text
Unable to activate type 'FishMMO.Database.Npgsql.NpgsqlDbContextFactory'.
The following constructors are ambiguous:
  Void .ctor(IConfiguration)
  Void .ctor(NpgsqlDbConfiguration)
```

**File:** `FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/Program.cs`  
(Apply the same pattern in Patcher if it registers the factory the same way.)

**Wrong:**

```csharp
services.AddSingleton(new NpgsqlDbConfiguration(context.Configuration));
services.AddSingleton<NpgsqlDbContextFactory>(); // DI picks constructor → ambiguous
```

**Right:**

```csharp
services.AddSingleton(new NpgsqlDbConfiguration(context.Configuration));
services.AddSingleton(sp =>
    new NpgsqlDbContextFactory(sp.GetRequiredService<NpgsqlDbConfiguration>()));
services.AddSingleton<INpgsqlDbContextFactory>(sp =>
    sp.GetRequiredService<NpgsqlDbContextFactory>());
```

Re-publish after the change.

---

## 4. Production `appsettings.Production.json` (publish output)

Must include **ForwardedHeaders** or the host dies after Kestrel starts:

```text
ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks must be configured in Production
```

Template (also under `FishMMO-Setup/Production/appsettings.IpFetchServer.Production.json`):

```json
{
  "WebServer": { "HttpPort": 8080 },
  "ConnectionStrings": { "AllowInsecureNpgsql": true },
  "Cors": {
    "AllowedOrigins": [
      "https://play.eqbrowser.com",
      "https://eqbrowser.com",
      "https://www.eqbrowser.com"
    ]
  },
  "ForwardedHeaders": {
    "KnownProxies": [ "127.0.0.1", "::1" ],
    "KnownNetworks": [
      "173.245.48.0/20",
      "103.21.244.0/22",
      "103.22.200.0/22",
      "103.31.4.0/22",
      "141.101.64.0/18",
      "108.162.192.0/18",
      "190.93.240.0/20",
      "188.114.96.0/20",
      "197.234.240.0/22",
      "198.41.128.0/17",
      "162.158.0.0/15",
      "104.16.0.0/13",
      "104.24.0.0/14",
      "172.64.0.0/13",
      "131.0.72.0/22"
    ]
  }
}
```

Copy into `$PUB/appsettings.Production.json` on deploy (or ensure publish picks it up from project).

---

## 5. Nginx: `api` + `ipfetch` hostnames

`/etc/nginx/sites-available/fishmmo` IPFetch vhost should list **both**:

```nginx
server_name api.eqbrowser.com ipfetch.eqbrowser.com;
```

`location /loginserver` and `/patchserver` proxy to `http://ipfetch_server` (`127.0.0.1:8080` in `conf.d/fishmmo.conf`).

```bash
sudo nginx -t && sudo systemctl reload nginx
```

Without `ipfetch.eqbrowser.com` on that server, CF may hit the apex HTML site (200 HTML) instead of the API.

---

## 6. `/loginserver` must return JSON (not empty directory)

Even with IPFetch healthy:

| Response | Meaning |
|----------|---------|
| **401** `Unauthorized.` | Missing/invalid `X-FishMMO-Client` (normal without signed client) |
| **404** `No login servers available.` | Table `login_servers` empty — Game LoginServer never registered |
| **200** `{"Ports":[7770],"ConnectionToken":"..."}` | Discovery OK |

### Seed / verify DB (emergency / until Unity re-registers)

```sql
-- as postgres superuser on fish_mmo_postgresql
INSERT INTO login_servers (name, address, port, time_created, last_pulse)
VALUES ('LoginServer', '<PUBLIC_IP>', 7770, NOW(), NOW())
ON CONFLICT (name) DO UPDATE
  SET address = EXCLUDED.address, port = EXCLUDED.port, last_pulse = NOW();
```

Prefer fixing Unity LoginServer so it **PersistAsync** on boot (see LoginServerSystem). Host also runs:

```bash
sudo systemctl start fishmmo-public-advertise-fix.service
# timer rewrites 127.0.0.1 → FISHMMO_PUBLIC_ADVERTISE_IP
```

### Required DB rows for IPFetch boot

```sql
SELECT key FROM deployment_secrets;           -- client_gate_secret, signing_key_kek
SELECT key_id, is_active FROM connection_token_keys;  -- shared HMAC for tokens
SELECT name, address, port FROM login_servers;        -- at least one row
```

Populate secrets via installer: **Database → Configure Server Keys**  
(or SQL equivalent). **Gate secret must match the client-built `ClientApiSecret.generated.cs`.**

---

## 7. Verification commands

```bash
systemctl is-active fishmmo-ipfetch
ss -tlnp | grep 8080

# Liveness (no client gate)
curl -sS http://127.0.0.1:8080/healthz
# expect: {"status":"ok","db":true,...}

# Through nginx (Host header)
curl -sS -H 'Host: api.eqbrowser.com' http://127.0.0.1/loginserver
# expect: Unauthorized.  (401) without X-FishMMO-Client — proves app, not 502

# Signed request (Python sketch) should return 200 + Ports JSON
# header: X-FishMMO-Client = v1.<unix_ts>.<nonce>.<base64url_hmac>
# canonical: v1\nGET\n/loginserver\n<ts>\n<nonce>
# HMAC-SHA256 key = UTF-8 bytes of client_gate_secret from DB
```

Public check from workstation:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' https://api.eqbrowser.com/loginserver
# 401 = good path; 502 = IPFetch still down; 200 only with valid client signature
```

---

## 8. Related: Linux GameServer package (so Unity build doesn’t break host)

When shipping a **new** dedicated server folder (e.g. `/var/www/src/GameServer`), post-build should:

1. Include **`libfishmmo_webtransport.so`** next to the player + under `GameServer_Data/Plugins/` (+ `x86_64/` if used), `chmod +x`.
2. Ship `appsettings.json` with **`Database: fish_mmo_postgresql`** (or omit Database and force env `FISHMMO_DB_NAME`) — never the bare name `fishmmo` if prod DB is `fish_mmo_postgresql`.
3. Ship `LoginServer.cfg` / `WorldServer.cfg` / `SceneServer.cfg` with ports **7770 / 7780 / 7790**, certs under `/etc/fishmmo/certs/`, `AllowedOrigins=https://play.eqbrowser.com`.
4. Do **not** ship a junk `Configuration.cfg` (e.g. `ServerType=Scene` leftover).
5. Do **not** overwrite host `fishmmo-secrets.env` on rsync.
6. Keep TotpMasterKey / auth init order fix in server binary (Login must bind 7770).
7. systemd wrappers: `WorkingDirectory` + `LD_LIBRARY_PATH` point at the new GameServer folder; start Login → World → Scene.

---

## 9. Patcher / WebGL (same class of bugs)

`fishmmo-patcher` historically used `/var/www/FishMMO-WebServers/...` and will **CHDIR-fail** the same way. Mirror §2–§4 for:

`FishMMO-WebServers/PatcherASP.NET/Patcher`

WebGL static client is separate (`play.eqbrowser.com` → `/var/www/html/play`); it does **not** use ClientGate on static assets.

---

## 10. Success criteria (client discovery)

- [ ] `fishmmo-ipfetch` **active**, listening `127.0.0.1:8080`
- [ ] `curl 127.0.0.1:8080/healthz` → **200** `db:true`
- [ ] nginx Host `api.eqbrowser.com` `/loginserver` → **401** without sig (not 502)
- [ ] signed `/loginserver` → **200** `{"Ports":[7770],"ConnectionToken":"..."}`
- [ ] Game Login process listening **UDP 7770** (and World/Scene 7780/7790 as needed)
- [ ] Client build uses same **gate secret** and **API host** (`api.eqbrowser.com` or `ipfetch.eqbrowser.com`)

---

## File map (this host)

| Item | Path |
|------|------|
| IPFetch publish | `/var/www/src/FishMMO/FishMMO-WebServers/IPFetchASP.NET/IpFetchServer/bin/Release/net8.0/publish` |
| systemd unit | `/etc/systemd/system/fishmmo-ipfetch.service` |
| nginx API vhost | `/etc/nginx/sites-available/fishmmo` |
| upstream | `/etc/nginx/conf.d/fishmmo.conf` → `127.0.0.1:8080` |
| DB secrets | `/etc/fishmmo/db-secrets.env` |
| GameServer | `/var/www/src/GameServer` |
| Production IpFetch template | `FishMMO-Setup/Production/appsettings.IpFetchServer.Production.json` |
