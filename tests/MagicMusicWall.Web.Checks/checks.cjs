const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

// Browser requests are intercepted. No live library, network server, credentials or audio are used.
const origin = 'http://127.0.0.1:18879';
const pageSource = path.resolve(__dirname, '../../src/MagicMusicWall.Streaming/RemotePage.html');
const results = path.resolve(process.env.MMW_WEB_RESULTS || path.join(__dirname, 'results'));
fs.mkdirSync(results, { recursive: true });

async function run() {
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let total = 6000, guest = false, offline = false, authExpired = false, delayOffset = -1;
    let releaseDelayed, delayedStarted;
    const commands = [];
    const item = index => ({ id: 'duplicate', title: 'Track ' + index, artist: 'Test Artist', album: 'Test Album', duration: '3:00', isVideo: false });
    await page.route(origin + '/**', async route => {
      const url = new URL(route.request().url());
      const endpoint = url.pathname;
      if (authExpired && endpoint.startsWith('/api/')) return route.fulfill({status:401,contentType:'application/json',body:JSON.stringify({error:'Session expired.'})});
      if (endpoint === '/auth/login') { authExpired = false; return route.fulfill({status:204}); }
      if (offline && endpoint.startsWith('/api/')) return route.abort('connectionrefused');
      if (endpoint === '/') return route.fulfill({ contentType: 'text/html', body: fs.readFileSync(pageSource, 'utf8') });
      let result;
      if (endpoint === '/auth/status') result = { authenticated: !authExpired, passphraseEnabled: true };
      else if (endpoint === '/api/access') result = { queueOnly: guest };
      else if (endpoint === '/api/state') result = {
        currentId: total ? 'duplicate' : null,
        title: 'Track ' + Math.max(0, total - 1), artist: 'Test Artist',
        queue: Array.from({ length: Math.min(total, 500) }, (_, i) => item(i)), queueTotal: total,
        currentQueueItem: total ? item(total - 1) : null, currentQueueIndex: total - 1,
        playlists: [], audioOutputs: [{ id: '', name: 'Default' }], browserPlaybackEnabled: !guest,
        volume: 50, durationMs: 180000, positionMs: 0
      };
      else if (endpoint === '/api/queue') {
        const offset = Number(url.searchParams.get('offset') || 0);
        if (offset === delayOffset) { delayedStarted?.(); await new Promise(resolve => { releaseDelayed = resolve; }); }
        result = { items: Array.from({ length: Math.max(0, Math.min(100, total - offset)) }, (_, i) => item(i + offset)), offset, total };
      }
      else if (endpoint === '/api/library') result = { items: [item(0), item(1)], total: 2 };
      else if (endpoint === '/api/folders') result = { folders: [], items: [item(0), item(1)], totalItems: 2, descendantCount: 2, title: 'Your folders', folderId: null, parentId: null };
      else if (endpoint === '/api/command') { commands.push(route.request().postDataJSON()); return route.fulfill({ status: 204 }); }
      else if (endpoint === '/api/stream') return route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ error: 'File is no longer available.' }) });
      else return route.fulfill({ status: 204 });
      await route.fulfill({ contentType: 'application/json', body: JSON.stringify(result) });
    });
    const waitRange = text => page.waitForFunction(expected => document.querySelector('#queueRange').textContent === expected, text);
    const waitConnected = () => page.waitForFunction(() => document.querySelector('#connection').textContent === 'Connected');
    await page.goto(origin);
    await waitRange('1–100 of 6000');
    assert.match(await page.title(), /Magic/);
    assert.equal(new URL(page.url()).origin, origin);
    await page.locator('#queueCurrentPage').click();
    await waitRange('5901–6000 of 6000');
    assert.equal(await page.locator('#queue .current').getAttribute('data-queue-index'), '5999');
    await page.locator('#queue .track-main').last().click();
    assert.equal(commands.at(-1).queueIndex, 5999);

    // A late prior page must never overwrite the newer page requested by the user.
    delayOffset = 5800;
    const started = new Promise(resolve => { delayedStarted = resolve; });
    await page.locator('#queuePreviousPage').click();
    await started;
    await page.locator('#queuePreviousPage').click();
    await waitRange('5701–5800 of 6000');
    delayOffset = -1;
    releaseDelayed();
    await page.waitForTimeout(100);
    assert.equal(await page.locator('#queueRange').textContent(), '5701–5800 of 6000');

    total = 250;
    await page.locator('#queuePreviousPage').click();
    await waitRange('201–250 of 250');
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: path.join(results, 'desktop.png'), fullPage: false });

    // Seed synthetic saved data once, then exercise real persistence on a subsequent reload.
    await page.addInitScript(() => {
      if (sessionStorage.getItem('seeded')) return;
      localStorage.setItem('magicmusic-browser-queue-v1', JSON.stringify({ version: 1, items: [
        { id: 'duplicate', title: 'Saved repeat', artist: 'Artist' },
        { id: 'duplicate', title: 'Saved repeat', artist: 'Artist' }
      ], index: 1 }));
      sessionStorage.setItem('seeded', 'true');
    });
    await page.reload(); await waitConnected(); await page.locator('#listenMode').click();
    assert.equal(await page.locator('#queue .track-row').count(), 2);
    assert.equal(await page.locator('#queue .current').getAttribute('data-queue-index'), '1');
    assert.equal(await page.locator('audio[src],video[src]').count(), 0, 'Restoring never starts audio or retains a stream URL');
    await page.locator('#clearQueue').click();
    assert.equal(await page.locator('#queue .track-row').count(), 0);
    await page.locator('#undoQueue').click();
    assert.equal(await page.locator('#queue .track-row').count(), 2);
    await page.reload(); await waitConnected(); await page.locator('#listenMode').click();
    assert.equal(await page.locator('#queue .track-row').count(), 2);

    const sentBeforeDisconnect = commands.length;
    offline = true;
    await page.waitForFunction(() => document.querySelector('#connection').textContent === 'Reconnecting…');
    assert.equal(await page.locator('#queue .track-row').count(), 2);
    offline = false; await waitConnected();
    assert.equal(commands.length, sentBeforeDisconnect, 'Reconnect must not replay mutations');
    assert.equal(await page.locator('#queue .track-row').count(), 2);
    await page.locator('#queue .track-main').first().click();
    await page.waitForFunction(() => document.querySelectorAll('#queue .track-row').length === 1);
    assert.match(await page.locator('#browserStatus').textContent(), /no longer available/);

    authExpired = true;
    await page.waitForFunction(() => !document.querySelector('#signIn').hidden);
    assert.equal(await page.evaluate(() => JSON.parse(localStorage.getItem('magicmusic-browser-queue-v1')).items.length), 1);
    await page.locator('#passphrase').fill('synthetic-test-passphrase');
    await page.locator('#signInSubmit').click(); await waitConnected();
    assert.equal(await page.locator('#queue .track-row').count(), 1, 'Reauthentication must retain the browser queue');
    assert.equal(await page.locator('audio[src],video[src]').count(), 0);

    guest = true; await page.reload();
    await page.waitForFunction(() => document.querySelector('#connection').textContent === 'Guest · connected');
    assert(await page.locator('#pcControls').isHidden());
    assert(await page.locator('#clearQueue').isHidden());
    assert(await page.locator('#listenMode').isDisabled());
    assert(await page.locator('#queue .track-main').first().isDisabled());
    assert(await page.locator('#nowInfo').isHidden());
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: path.join(results, 'phone.png'), fullPage: false });
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    assert.deepEqual(errors, []);
    console.log('PASS: 6000-track paging, duplicates, absolute play, stale responses, shrink, persistence, undo, reconnect, reauthentication, removed files, guest UI, phone layout, JavaScript health.');
  } finally { await browser.close(); }
}
run().catch(error => { console.error(error); process.exitCode = 1; });
