# Web remote browser checks

These checks run the repository's actual embedded HTML in Chromium with intercepted synthetic API responses. They do not connect to a running music player, read personal libraries, or play audio. Real HTTP authorization is tested separately in `MagicMusicWall.Streaming.Tests`.

Requirements: Node.js 20 or newer and npm. From this directory:

```powershell
npm ci
npx playwright install chromium
npm test
```

Playwright 1.61.1 is pinned in the lockfile. Dependencies install only in this test directory; no global package or production dependency is needed. Browser installation uses Playwright's normal per-user cache. On a fresh Linux machine use `npx playwright install --with-deps chromium` if system dependencies are missing.

Checks include 6,000-track pagination, exact duplicate occurrences, absolute play indices, late page responses, shrinking queues, persisted browser queues, clear/undo, reconnect without command replay, queue retention through session expiry and reauthentication, stale-file removal, guest controls, desktop and 390px phone layout, and uncaught JavaScript errors. Requests and media IDs are synthetic.

Screenshots are saved to ignored `results/`, or the directory specified by `MMW_WEB_RESULTS`. They are test evidence, not documentation screenshots. The maintained documentation screenshots are captured from an isolated running desktop application and its real remote bridge.

To test the server's authorization and paging contracts from the repository root:

```powershell
dotnet test tests/MagicMusicWall.Streaming.Tests -c Release
```

These browser mocks do not prove codec support, hardware routing, real guest authorization, or behavior of remote browsers other than the installed Chromium build.
