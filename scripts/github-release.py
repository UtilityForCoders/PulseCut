"""Publish a GitHub release using Git Credential Manager without storing credentials."""
import argparse
import hashlib
import http.client
import json
import os
from pathlib import Path
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', default='UtilityForCoders/PulseCut')
    parser.add_argument('--tag', default='v0.1.0')
    parser.add_argument('--target')
    parser.add_argument('--notes', default='RELEASE_NOTES.md')
    parser.add_argument('--check', action='store_true')
    parser.add_argument('assets', nargs='*')
    args = parser.parse_args()
    if not args.check and (not args.target or not args.assets):
        parser.error('--target and asset paths are required to publish')
    env = dict(os.environ, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='Never')
    result = subprocess.run(['git', 'credential', 'fill'],
        input=f'protocol=https\nhost=github.com\npath={args.repo}.git\n\n',
        text=True, capture_output=True, env=env)
    credential = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
    token = credential.get('password')
    if result.returncode or not token:
        raise RuntimeError('Git Credential Manager did not provide GitHub authentication.')
    headers = {'Authorization': f'Bearer {token}', 'Accept': 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28', 'User-Agent': 'PulseCut-release'}
    endpoint = f'https://api.github.com/repos/{args.repo}'

    def api(method, suffix, body=None):
        data = None if body is None else json.dumps(body).encode('utf-8')
        request = urllib.request.Request(endpoint + suffix, data=data, method=method,
            headers={**headers, 'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code == 404 and method == 'GET':
                return None
            raise RuntimeError(f'GitHub API returned HTTP {error.code} for {method} {suffix}.') from None

    repo = api('GET', '')
    if not repo or not repo.get('permissions', {}).get('push'):
        raise RuntimeError('The authenticated GitHub account cannot publish to this repository.')
    tag = urllib.parse.quote(args.tag, safe='')
    release = api('GET', f'/releases/tags/{tag}')
    # Draft releases are not always discoverable through the tag endpoint.
    if not release:
        release = next((item for item in api('GET', '/releases?per_page=100') or []
            if item['tag_name'] == args.tag), None)
    if args.check:
        print(json.dumps({'repository': repo['full_name'], 'can_push': True,
            'release': None if not release else {'id': release['id'], 'draft': release['draft'],
            'url': release['html_url'], 'assets': [a['name'] for a in release['assets']]}}))
        return

    assets = [Path(name).resolve(strict=True) for name in args.assets]
    if len({asset.name for asset in assets}) != len(assets):
        raise RuntimeError('Asset names must be unique.')
    # Confirm the public tag points to the exact tested source revision.
    ref = api('GET', f'/git/ref/tags/{tag}')
    if not ref:
        raise RuntimeError('Push the release tag before publishing.')
    obj = ref['object']
    while obj['type'] == 'tag':
        obj = api('GET', f'/git/tags/{obj["sha"]}')['object']
    if obj['sha'] != args.target:
        raise RuntimeError('The remote tag does not match --target.')
    notes = Path(args.notes).read_text(encoding='utf-8')
    if not release:
        release = api('POST', '/releases', {'tag_name': args.tag, 'target_commitish': args.target,
            'name': f'PulseCut {args.tag.removeprefix("v")}', 'body': notes,
            'draft': True, 'prerelease': False})
        print(f'Created draft release {release["id"]}.', flush=True)
    if release['draft']:
        existing = {asset['name']: asset for asset in release['assets']}
        upload = urllib.parse.urlparse(release['upload_url'].split('{')[0])
        if upload.scheme != 'https' or upload.hostname != 'uploads.github.com':
            raise RuntimeError('Unexpected GitHub upload host.')
        for asset in assets:
            with asset.open('rb') as source:
                digest = hashlib.file_digest(source, 'sha256').hexdigest()
            if asset.name in existing:
                previous = existing[asset.name]
                if previous['size'] != asset.stat().st_size or previous.get('digest') != f'sha256:{digest}':
                    raise RuntimeError(f'The draft contains a different asset: {asset.name}.')
                print(f'Already uploaded: {asset.name}', flush=True)
                continue
            connection = http.client.HTTPSConnection(upload.hostname, timeout=180)
            route = upload.path + '?' + urllib.parse.urlencode({'name': asset.name})
            connection.putrequest('POST', route)
            for key, value in headers.items():
                connection.putheader(key, value)
            connection.putheader('Content-Type', 'application/octet-stream')
            connection.putheader('Content-Length', str(asset.stat().st_size))
            connection.endheaders()
            print(f'Uploading {asset.name} ({asset.stat().st_size} bytes).', flush=True)
            with asset.open('rb') as stream:
                for chunk in iter(lambda: stream.read(1024 * 1024), b''):
                    connection.send(chunk)
            response = connection.getresponse()
            response_data = response.read()
            connection.close()
            if response.status != 201:
                raise RuntimeError(f'GitHub upload returned HTTP {response.status} for {asset.name}.')
            uploaded = json.loads(response_data)
            if uploaded['size'] != asset.stat().st_size or uploaded.get('digest') != f'sha256:{digest}':
                raise RuntimeError(f'GitHub did not confirm the checksum of {asset.name}.')
            print(f'Checksum verified: {asset.name}', flush=True)
        release = api('PATCH', f'/releases/{release["id"]}',
            {'body': notes, 'draft': False, 'make_latest': 'true'})
    verified = api('GET', f'/releases/{release["id"]}')
    uploaded = {asset['name']: asset for asset in verified['assets']}
    if verified['draft'] or not all(asset.name in uploaded for asset in assets):
        raise RuntimeError('The published release is missing required assets.')
    print(json.dumps({'published': True, 'url': verified['html_url'], 'tag': verified['tag_name'],
        'assets': [{'name': asset['name'], 'size': asset['size'], 'url': asset['browser_download_url']}
            for asset in verified['assets']]}, indent=2))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(f'Release failed: {error}', file=sys.stderr)
        sys.exit(1)
