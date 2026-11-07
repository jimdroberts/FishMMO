"""Run: python3 -m unittest discover -s FishMMO-Installer/tests -v"""
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

SCRIPT = Path(__file__).resolve().parents[1] / 'FishMMO-Installer/Resources/porkbun-dns-hook.py'
spec = importlib.util.spec_from_file_location('porkbun_dns_hook', SCRIPT)
hook = importlib.util.module_from_spec(spec)
spec.loader.exec_module(hook)


class FakeResponse(io.BytesIO):
    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


class PorkbunHookTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.credentials = Path(self.directory.name) / 'porkbun.ini'
        self.credentials.write_text('# comment\nPORKBUN_API_KEY=pk1_a\nPORKBUN_SECRET_API_KEY="sk1_b"\n'
                                    'PORKBUN_DOMAIN=Example.com.\n')
        self.env = patch.dict(os.environ, {'FISHMMO_PORKBUN_CREDENTIALS': str(self.credentials)})
        self.env.start()

    def tearDown(self):
        self.env.stop()
        self.directory.cleanup()

    def test_credentials_are_parsed_and_zone_normalised(self):
        values = hook.load_credentials()
        self.assertEqual(values['PORKBUN_SECRET_API_KEY'], 'sk1_b')
        self.assertEqual(values['PORKBUN_DOMAIN'], 'example.com')

    def test_missing_credential_fails(self):
        self.credentials.write_text('PORKBUN_API_KEY=pk1_a\n')
        with self.assertRaises(SystemExit):
            hook.load_credentials()

    def test_record_names_are_relative_to_the_zone(self):
        self.assertEqual(hook.record_name('example.com', 'example.com'), '_acme-challenge')
        self.assertEqual(hook.record_name('*.example.com', 'example.com'), '_acme-challenge')
        self.assertEqual(hook.record_name('API.example.com.', 'example.com'), '_acme-challenge.api')
        with self.assertRaises(SystemExit):
            hook.record_name('example.org', 'example.com')
        with self.assertRaises(SystemExit):
            hook.record_name('notexample.com', 'example.com')

    def test_auth_creates_record_prints_id_and_waits_for_every_nameserver(self):
        calls = []

        def fake_call(path, credentials, body=None):
            calls.append((path, body))
            return {'status': 'SUCCESS', 'id': 4242}

        answers = {('NS', 'example.com'): ['a.ns.porkbun.com.', 'b.ns.porkbun.com.']}
        polls = {'n': 0}

        def fake_dig(*args):
            if args[0] == 'NS':
                return answers[args]
            polls['n'] += 1
            # The second nameserver lags one round.
            if args[2] == '@b.ns.porkbun.com' and polls['n'] < 3:
                return []
            return ['"other"', '"token-value"']

        env = {'CERTBOT_DOMAIN': '*.example.com', 'CERTBOT_VALIDATION': 'token-value'}
        stdout = io.StringIO()
        with patch.dict(os.environ, env), patch.object(hook, 'call', fake_call), \
                patch.object(hook, 'dig', fake_dig), patch.object(hook.shutil, 'which', return_value='/usr/bin/dig'), \
                patch.object(hook.time, 'sleep'), patch('sys.stdout', stdout):
            hook.auth()
        self.assertEqual(calls, [('dns/create/example.com',
                                  {'name': '_acme-challenge', 'type': 'TXT', 'content': 'token-value', 'ttl': 600})])
        self.assertEqual(stdout.getvalue().strip(), '4242')
        self.assertGreaterEqual(polls['n'], 3)

    def test_cleanup_deletes_by_the_id_auth_printed(self):
        calls = []
        env = {'CERTBOT_DOMAIN': 'example.com', 'CERTBOT_VALIDATION': 'v', 'CERTBOT_AUTH_OUTPUT': '4242\n'}
        with patch.dict(os.environ, env), patch.object(hook, 'call', lambda p, c, b=None: calls.append(p) or {}):
            hook.cleanup()
        self.assertEqual(calls, ['dns/delete/example.com/4242'])

    def test_cleanup_without_id_matches_name_and_value(self):
        calls = []

        def fake_call(path, credentials, body=None):
            calls.append(path)
            if path.startswith('dns/retrieveByNameType'):
                return {'records': [{'id': '7', 'content': 'other'}, {'id': '8', 'content': '"mine"'}]}
            return {}

        env = {'CERTBOT_DOMAIN': 'play.example.com', 'CERTBOT_VALIDATION': 'mine', 'CERTBOT_AUTH_OUTPUT': ''}
        with patch.dict(os.environ, env), patch.object(hook, 'call', fake_call):
            hook.cleanup()
        self.assertEqual(calls, ['dns/retrieveByNameType/example.com/TXT/_acme-challenge.play',
                                 'dns/delete/example.com/8'])

    def test_cleanup_failure_never_fails_certbot(self):
        env = {'CERTBOT_DOMAIN': 'example.com', 'CERTBOT_VALIDATION': 'v', 'CERTBOT_AUTH_OUTPUT': '1'}

        def refuse(*args, **kwargs):
            raise SystemExit(1)

        with patch.dict(os.environ, env), patch.object(hook, 'call', refuse):
            hook.cleanup()  # must return normally

    def test_call_sends_keys_in_body_and_retries_rate_limit(self):
        seen = []

        def fake_urlopen(request, timeout):
            seen.append(json.loads(request.data))
            if len(seen) == 1:
                raise urllib.error.HTTPError(request.full_url, 429, 'slow down', {'Retry-After': '1'}, io.BytesIO(b''))
            return FakeResponse(b'{"status":"SUCCESS","id":1}')

        with patch.object(hook.urllib.request, 'urlopen', fake_urlopen), patch.object(hook.time, 'sleep'):
            result = hook.call('dns/create/example.com', hook.load_credentials(), {'name': 'x'})
        self.assertEqual(result['id'], 1)
        self.assertEqual(len(seen), 2)
        self.assertEqual(seen[0]['apikey'], 'pk1_a')
        self.assertEqual(seen[0]['secretapikey'], 'sk1_b')

    def test_api_refusal_fails(self):
        def fake_urlopen(request, timeout):
            return FakeResponse(b'{"status":"ERROR","code":"API_ACCESS_DISABLED","message":"not opted in"}')

        with patch.object(hook.urllib.request, 'urlopen', fake_urlopen), self.assertRaises(SystemExit):
            hook.call('dns/create/example.com', hook.load_credentials())

    def run_records(self, args, zone_records):
        """Runs 'records' against an in-memory zone: {(type, name): [content, ...]}."""
        calls = []

        def fake_call(path, credentials, body=None, base=hook.API):
            calls.append((path, body, base))
            if path == 'ping':
                return {'yourIp': '203.0.113.7'}
            if path.startswith('dns/retrieveByNameType/'):
                _, _, _, rtype, name = path.split('/')
                return {'records': [{'id': '1', 'content': c} for c in zone_records.get((rtype, name), [])]}
            return {}

        stdout = io.StringIO()
        with patch.object(hook, 'call', fake_call), patch('sys.stdout', stdout):
            code = hook.records(args)
        return code, calls, stdout.getvalue()

    def test_records_creates_updates_and_skips(self):
        zone = {('A', 'api'): ['203.0.113.7'], ('A', 'play'): ['198.51.100.1'], ('CNAME', 'panel'): ['x.example.net']}
        code, calls, out = self.run_records(['--ip', '203.0.113.7', 'api', 'play', 'game.example.com', 'panel'], zone)
        writes = [(p, b) for p, b, _ in calls if not p.startswith('dns/retrieve')]
        self.assertEqual(writes, [
            ('dns/editByNameType/example.com/A/play', {'content': '203.0.113.7', 'ttl': 600}),
            ('dns/create/example.com', {'name': 'game', 'type': 'A', 'content': '203.0.113.7', 'ttl': 600}),
        ])
        self.assertIn('api.example.com: A 203.0.113.7 (already correct)', out)
        self.assertIn('panel.example.com: is a CNAME', out)
        self.assertEqual(code, 1)  # the CNAME needs attention

    def test_records_default_names_and_address_from_ipv4_ping(self):
        code, calls, _ = self.run_records([], {})
        self.assertEqual(calls[0][0], 'ping')
        self.assertEqual(calls[0][2], hook.API_IPV4)
        created = [b['name'] for p, b, _ in calls if p == 'dns/create/example.com']
        self.assertEqual(created, ['api', 'play', 'game', 'panel'])
        self.assertEqual(code, 0)

    def test_records_dry_run_writes_nothing(self):
        code, calls, out = self.run_records(['--dry-run', '--ip', '203.0.113.7', 'api'], {('A', 'api'): ['198.51.100.1']})
        self.assertTrue(all(p.startswith('dns/retrieve') for p, _, _ in calls))
        self.assertIn('[dry run, not changed]', out)

    def test_records_refuses_private_addresses_and_apex_names(self):
        for address in ('192.168.1.2', '10.0.0.1', '100.64.1.1', '172.16.0.1', '127.0.0.1', 'not-an-ip'):
            with self.assertRaises(SystemExit, msg=address):
                self.run_records(['--ip', address, 'api'], {})
        for name in ('@', 'example.com', '*', '*.api', 'bad_name'):
            with self.assertRaises(SystemExit, msg=name):
                self.run_records(['--ip', '203.0.113.7', name], {})


if __name__ == '__main__':
    unittest.main()
