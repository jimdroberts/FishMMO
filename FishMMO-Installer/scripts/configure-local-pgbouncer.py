#!/usr/bin/env python3
"""Configure local PgBouncer and switch development runtimes after a successful query.

Run with sudo and --apply. PostgreSQL remains on 5432; migrations are unchanged.
Uses existing application credentials, without modifying database roles.
"""
import argparse
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import subprocess
import tempfile
import time


def read_secrets(path):
    values = {}
    for raw in path.read_text().splitlines():
        if raw.strip() and not raw.lstrip().startswith('#') and '=' in raw:
            key, value = raw.split('=', 1)
            values[key.strip()] = value.strip()
    for key in ('FISHMMO_DB_USERNAME', 'FISHMMO_DB_PASSWORD'):
        if not values.get(key):
            raise RuntimeError(f'{key} is missing from {path}')
    return values


def query(port, credentials):
    env = os.environ.copy()
    env.update(PGPASSWORD=credentials['FISHMMO_DB_PASSWORD'], PGCONNECT_TIMEOUT='5', PGSSLMODE='disable')
    # Explicit connection arguments avoid inherited PGHOST/PGPORT/PGDATABASE overrides.
    result = subprocess.run(['psql', '-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1',
                             '-h', '127.0.0.1', '-p', str(port),
                             '-U', credentials['FISHMMO_DB_USERNAME'], '-d', 'fishmmo',
                             '-c', 'SELECT 1'], env=env, capture_output=True, text=True)
    if result.returncode or result.stdout.strip() != '1':
        # Do not print DSNs, passwords, or credential-file contents.
        raise RuntimeError(f'Application SELECT 1 failed on port {port}; check database/service logs. Runtime settings were not switched.')


def wait_for_pool(credentials):
    # Type=simple systemd services can be active before their socket is ready.
    for attempt in range(20):
        try:
            query(6432, credentials)
            return
        except RuntimeError:
            if attempt == 19:
                raise
            time.sleep(0.25)


def config_text(username, verifier, *, is_verifier=True):
    if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', username):
        raise RuntimeError('The local setup command requires a simple PostgreSQL role name.')
    if not verifier or any(character in verifier for character in ('\n', '\r', '\0')):
        raise RuntimeError('Application credential is empty or contains an unsupported control character.')
    if is_verifier and (not verifier.startswith('SCRAM-SHA-256$') or '"' in verifier):
        raise RuntimeError('Application role has no valid SCRAM verifier. No configuration was changed.')
    ini = '''# fishmmo-managed local PgBouncer configuration
[databases]
fishmmo = host=127.0.0.1 port=5432 dbname=fishmmo

[pgbouncer]
listen_addr = 127.0.0.1
listen_port = 6432
auth_type = scram-sha-256
auth_file = /etc/pgbouncer/userlist.txt
pool_mode = transaction
max_client_conn = 1000
default_pool_size = 25
reserve_pool_size = 5
reserve_pool_timeout = 3
ignore_startup_parameters = extra_float_digits
; Log to systemd's journal. No logfile or pidfile directory is required.
'''
    # PgBouncer's auth file escapes embedded quotes by doubling them.
    encoded = verifier.replace('"', '""')
    return ini, f'"{username}" "{encoded}"\n'


def atomic_write(path, content, mode=None, owner=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    original = path.stat() if path.exists() else None
    fd, name = tempfile.mkstemp(prefix='.fishmmo-', dir=path.parent)
    try:
        with os.fdopen(fd, 'w') as output:
            output.write(content)
            os.fchmod(output.fileno(), mode if mode is not None else (original.st_mode & 0o777 if original else 0o644))
            if owner:
                os.fchown(output.fileno(), *owner)
            elif original:
                os.fchown(output.fileno(), original.st_uid, original.st_gid)
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


def update_runtime(path, backup_root):
    data = json.loads(path.read_text()) if path.exists() else {}
    settings = data.setdefault('Npgsql', {})
    settings.update(Host='127.0.0.1', Port='6432')
    # IPFetch's Development DSN is also used by its configuration guard.
    strings = data.get('ConnectionStrings', {})
    if isinstance(strings.get('NpgsqlConnection'), str):
        strings['NpgsqlConnection'] = re.sub(r'(?i)\bPort\s*=\s*\d+', 'Port=6432', strings['NpgsqlConnection'])
    content = json.dumps(data, indent=2, ensure_ascii=False) + '\n'
    if not path.exists() or json.loads(path.read_text()) != data:
        if path.exists():
            backup = backup_root / path.relative_to(path.anchor)
            if not backup.exists():
                backup.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, backup)
                os.chmod(backup, 0o600)
        # New files in the repository belong to its user, rather than root.
        parent = path.parent.stat()
        atomic_write(path, content, owner=(parent.st_uid, parent.st_gid))
        print(f'Runtime port set to 6432: {path}')


def runtime_paths(repo):
    dev = repo / 'FishMMO-Setup/Development'
    names = ('appsettings.json', 'appsettings.DiscordBot.json', 'appsettings.IpFetchServer.json',
             'appsettings.IpFetchServer.Development.json', 'appsettings.Patcher.json',
             'appsettings.ControlPanel.json')
    paths = [dev / name for name in names]
    paths.append(repo / 'FishMMO-Unity/appsettings.Development.json')
    # Existing binaries receive the same settings; future builds copy the templates.
    for folder in ('FishMMO-WebServers/IPFetchASP.NET/IpFetchServer',
                   'FishMMO-WebServers/PatcherASP.NET/Patcher',
                   'FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel',
                   'FishMMO-DiscordBot/FishMMO-DiscordBot'):
        if folder == 'FishMMO-WebServers/IPFetchASP.NET/IpFetchServer':
            paths.extend((repo / 'Builds/IpFetchServer').rglob('appsettings.json'))
        elif folder == 'FishMMO-WebServers/PatcherASP.NET/Patcher':
            paths.extend((repo / 'Builds/Patcher').rglob('appsettings.json'))
        elif folder == 'FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel':
            paths.extend((repo / 'Builds/ControlPanel').rglob('appsettings.json'))
        elif folder == 'FishMMO-DiscordBot/FishMMO-DiscordBot':
            paths.extend((repo / 'Builds/FishMMO-DiscordBot').rglob('appsettings.json'))
        if 'IpFetchServer' in folder:
            paths.extend((repo / 'Builds/IpFetchServer').rglob('appsettings.Development.json'))
    for player in ('Server', 'Client'):
        paths.extend((repo / 'Builds' / player).rglob('appsettings.json'))
    return list(dict.fromkeys(paths))


def apply(repo):
    if os.geteuid() != 0:
        raise RuntimeError('Run this command with sudo --apply to configure the system service.')
    credentials = read_secrets(Path('/etc/fishmmo/db-secrets.env'))
    query(5432, credentials)
    print('Existing application credentials verified against PostgreSQL on 5432.')
    # Local peer authentication as the PostgreSQL OS account: no administrator password needed.
    result = subprocess.run(['runuser', '-u', 'postgres', '--', 'psql', '-X', '-w', '-qAt',
                             '-v', 'ON_ERROR_STOP=1', '-v', 'role=' + credentials['FISHMMO_DB_USERNAME'],
                             '-p', '5432', '-d', 'postgres'],
                            input="SELECT rolpassword FROM pg_catalog.pg_authid WHERE rolname = :'role' AND rolcanlogin AND (rolvaliduntil IS NULL OR rolvaliduntil > CURRENT_TIMESTAMP);\n",
                            capture_output=True, text=True,
                            env={'PATH': os.environ.get('PATH', '/usr/bin:/bin'), 'PGCONNECT_TIMEOUT': '5'})
    verifier = result.stdout.strip()
    if result.returncode == 0 and verifier.startswith('SCRAM-SHA-256$'):
        ini, userlist = config_text(credentials['FISHMMO_DB_USERNAME'], verifier)
    else:
        # SCRAM on local sockets may prevent verifier lookup without an admin
        # password. PgBouncer also supports plaintext credentials for SCRAM,
        # including backend authentication. Reuse the existing credential in a
        # 0600 file rather than requiring admin credentials or weakening pg_hba.
        ini, userlist = config_text(credentials['FISHMMO_DB_USERNAME'],
                                   credentials['FISHMMO_DB_PASSWORD'], is_verifier=False)
        print('Using existing application credentials in a pgbouncer-owned 0600 auth file.')
    # Parse all runtime JSON before touching the service, so invalid JSON fails early.
    paths = runtime_paths(repo)
    for path in paths:
        if path.exists():
            json.loads(path.read_text())
    account = pwd.getpwnam('pgbouncer')
    ini_path = Path('/etc/pgbouncer/pgbouncer.ini')
    user_path = Path('/etc/pgbouncer/userlist.txt')
    # Keep both the original installation backup and an immediate rollback copy.
    rollback = {}
    for path in (ini_path, user_path):
        rollback[path] = (path.read_bytes(), path.stat()) if path.exists() else None
        backup = path.with_name(path.name + '.pre-pgbouncer.bak')
        if path.exists() and not backup.exists():
            shutil.copy2(path, backup)
            os.chmod(backup, 0o600)
    try:
        atomic_write(user_path, userlist, 0o600, (account.pw_uid, account.pw_gid))
        atomic_write(ini_path, ini, 0o640, (account.pw_uid, account.pw_gid))
        subprocess.run(['systemctl', 'restart', 'pgbouncer'], check=True)
        wait_for_pool(credentials)
        subprocess.run(['systemctl', 'is-active', '--quiet', 'pgbouncer'], check=True)
    except Exception:
        for path, original in rollback.items():
            if original:
                content, attributes = original
                atomic_write(path, content.decode(), attributes.st_mode & 0o777,
                             (attributes.st_uid, attributes.st_gid))
            elif path.exists():
                path.unlink()
        subprocess.run(['systemctl', 'restart', 'pgbouncer'], check=False, capture_output=True)
        raise
    print('PgBouncer is running on 6432. Application authentication and SELECT 1 passed.')
    subprocess.run(['systemctl', 'enable', 'pgbouncer'], check=True)
    backup_root = Path('/var/backups/fishmmo-pgbouncer')
    backup_root.mkdir(mode=0o700, parents=True, exist_ok=True)
    for path in paths:
        update_runtime(path, backup_root)
    print('Development runtime settings now use 6432. PostgreSQL, installer and migration defaults remain on 5432.')
    print('Restart any running game servers or web applications to use the updated settings.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true', help='Configure the system service and then switch local runtime settings.')
    parser.add_argument('--repo-root', type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    try:
        if not args.apply:
            print('Plan: existing application credentials → loopback PgBouncer 6432 → PostgreSQL 5432.')
            print('Runtime settings will change only after a successful application SELECT 1 through PgBouncer.')
            for path in runtime_paths(args.repo_root):
                print(path)
            return
        apply(args.repo_root)
    except (RuntimeError, OSError, subprocess.SubprocessError, ValueError) as error:
        parser.exit(1, f'Local PgBouncer setup failed: {error}\n')


if __name__ == '__main__':
    main()
