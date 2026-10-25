# Security headers regression checks

Run `dotnet run --project FishMMO-WebServers/Tools/SecurityHeaders.Tests` from the
repository root. Uses the shipping shared middleware with a real ephemeral-loopback
Kestrel server and static files; no external test framework or service is required.
Checks Unity script hashing/line endings/rebuilds, wasm/blob worker allowances,
API and panel isolation, production restrictions, and configuration injection.


## Real WebGL client browser check

With the WebGL server and game client already built, install the test dependency
with `npm install` in this directory, then run `npm run test:browser`.
Install Chromium using `npx playwright install chromium` if it is not already available.
No Unity build is started. The test starts its own server on an ephemeral loopback
port, uses a fresh browser context, and closes both when finished.

Optional environment variables:

- `FISHMMO_WEBGL_SERVER`: absolute path to the WebGLServer executable.
- `FISHMMO_WEBGL_ROOT`: directory containing the game client's `index.html`.
- `FISHMMO_CHROMIUM_EXECUTABLE`: use an existing Chromium installation.
- `FISHMMO_PLAYWRIGHT_MODULE`: use an existing Playwright module directory.
- `FISHMMO_CSP_ARTIFACTS`: output directory for the JSON report, server log, and screenshot.

The test requires Unity's actual initialization promise to resolve, checks for CSP
violations during startup and local API/WebTransport probes, and confirms that an
untrusted inline script is blocked. The negative control is reported separately
from game violations. It also records WebGPU adapter availability, cross-origin
isolation, SharedArrayBuffer, service-worker startup, and resource responses.
The software GPU flags support running this in headless Chromium.

A connection refusal from an offline API is recorded separately and does not imply
a CSP failure. The probes do not authenticate or create a game session; the test
checks connection permissions, not API health, certificates, or authenticated
login/gameplay. A service-worker startup event does not prove offline cache installation.


Verified on 2026-10-25 against the actual built WebGPU game client: Unity reached
`ready`, 70 resource responses included 60 Addressables responses, with zero CSP
violations, JavaScript errors or HTTP error statuses. WebGPU adapter availability,
cross-origin isolation and SharedArrayBuffer were confirmed. An injected untrusted
inline-script control was blocked. See `Evidence/2026-10-25/game-client-csp.json`.
The API at localhost:8088 was offline, so live login/gameplay remains unverified.
