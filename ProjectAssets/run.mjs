// Trusted host oracle, mounted read-only outside the generated project.
import { cp, mkdir, readFile, mkdtemp, readdir, writeFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import { tmpdir, networkInterfaces } from 'node:os';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

export async function runOracle(profile, source, { browser = true, build = true } = {}) {
  if (profile === 'security') {
    for (const target of ['/project/.oracle-write-probe', '/oracle/.oracle-write-probe']) {
      await assert.rejects(writeFile(target, 'probe'), error => ['EROFS', 'EACCES'].includes(error.code));
    }
    assert.notEqual(process.getuid(), 0);
    assert.match(await readFile('/proc/self/status','utf8'), /CapEff:\s+0+\s/);
    assert.equal((await readFile('/sys/fs/cgroup/memory.max','utf8')).trim(), String(768*1024*1024));
    assert.equal((await readFile('/sys/fs/cgroup/pids.max','utf8')).trim(), '128');
    assert.ok(Object.keys(networkInterfaces()).every(name => name === 'lo'));
    assert.ok(Object.keys(process.env).every(name => !/^(NVIDIA_API_KEY|NVAPI_KEY|OPENAI_API_KEY)$/.test(name)));
    return {passed:true,checks:['project-readonly','oracle-readonly','nonroot','capabilities-dropped','memory-limit','pid-limit','network-none','no-provider-key-env']};
  }
  const work = await mkdtemp(join(tmpdir(), 'agentloop-pos-'));
  await cp(source, work, { recursive: true, filter: p => !/(?:^|[\\/])(?:node_modules|\.git|local-secrets|\.agentloop)(?:[\\/]|$)/.test(p) });
  const command = async (exe, args) => {
    const child = spawn(exe, args, { cwd: work, env: { PATH: process.env.PATH, HOME: work, npm_config_cache: join(work, '.npm') }, stdio: ['ignore', 'pipe', 'pipe'] });
    let log = '';
    for (const stream of [child.stdout, child.stderr]) stream.on('data', data => { if (log.length < 32000) log += data.toString().slice(0, 32000 - log.length); });
    const done = once(child, 'exit');
    child.on('error', () => {});
    const [code] = await done;
    assert.equal(code, 0, `command failed (${code}): ${log}`);
  };
  if (build) {
    await command('npm', ['ci', '--ignore-scripts', '--offline', '--no-audit', '--no-fund']);
    if (profile === 'install') return { passed: true, checks: ['offline-install'] };
    await command('npm', ['run', 'build']);
    const sources = [];
    const scan = async path => {for (const item of await readdir(path, {withFileTypes:true})) {const file=join(path,item.name); if(item.isDirectory()) await scan(file); else if(item.isFile() && item.name.endsWith('.ts')) sources.push(file);}};
    await scan(join(work,'src'));
    assert.ok(sources.length, 'TypeScript sources required');
    await command('/usr/local/bin/tsc', ['--noEmit','--strict','--allowImportingTsExtensions','--module','NodeNext','--moduleResolution','NodeNext','--target','ES2022','--lib','ES2022,DOM', ...sources]);
    if (profile === 'build') return { passed: true, checks: ['offline-install', 'build'] };
  }
  const data = join(work, 'oracle-data');
  await mkdir(data);
  let server;
  const start = async () => {
    server = spawn(process.execPath, ['src/servidor.ts'], { cwd: work, env: { PATH: process.env.PATH, HOME: work, POS_DB: join(data, 'pos.sqlite'), PORT: '0', HOST: '127.0.0.1' }, stdio: ['ignore', 'pipe', 'pipe'] });
    let text = ''; let diagnostics = '';
    const ready = new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('service readiness timeout')), 10000);
      server.once('error', error => { clearTimeout(timer); reject(error); });
      server.once('exit', code => { clearTimeout(timer); reject(new Error(`service exited before ready: ${code}; ${diagnostics}`)); });
      server.stdout.on('data', data => {
        text = (text + data.toString()).slice(-16000);
        const match = /POS_READY (\d+)/.exec(text);
        if (match) { clearTimeout(timer); resolve(`http://127.0.0.1:${match[1]}`); }
      });
      server.stderr.on('data', data => {diagnostics=(diagnostics+data.toString()).slice(-4000);}); // Drain; generated logs cannot attest success.
    });
    return await ready;
  };
  const stop = async () => {
    if (!server || server.exitCode !== null) return;
    const ended = once(server, 'exit');
    server.kill('SIGTERM');
    const timer = setTimeout(() => server.kill('SIGKILL'), 2000);
    await ended; clearTimeout(timer);
  };
  const checks = [];
  let base;
  const api = async (path, body, expected = 200) => {
    const response = await fetch(base + path, { method: body ? 'POST' : 'GET', headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(3000) });
    assert.equal(response.status, expected, `${path} status`);
    return response.json();
  };
  try {
    base = await start();
    assert.equal((await api('/health')).ok, true); checks.push('health');
    if (profile === 'health') return { passed: true, checks };
    const product = await api('/api/products', { name: 'Producto de prueba', priceCents: 2500, stock: 10 }, 201);
    assert.ok(Number.isInteger(product.id));
    const stored = await api('/api/products');
    assert.ok(stored.some(p => p.id === product.id && p.stock === 10)); checks.push('catalog');
    await api('/api/sales', { key: 'fail-atomic', items: [{ productId: product.id, quantity: 2 }], paidCents: 1000 }, 400);
    assert.equal((await api('/api/products')).find(p => p.id === product.id).stock, 10);
    assert.equal((await api('/api/sales')).length, 0); checks.push('atomic-rejection');
    await api('/api/sales', { key: 'insufficient', items: [{ productId: product.id, quantity: 11 }], paidCents: 100000 }, 409);
    assert.equal((await api('/api/products')).find(p => p.id === product.id).stock, 10); checks.push('insufficient-stock');
    const input = { key: 'sale-1', items: [{ productId: product.id, quantity: 2 }], paidCents: 10000 };
    const sale = await api('/api/sales', input, 201);
    assert.equal(sale.totalCents, 5000); assert.equal(sale.changeCents, 5000);
    assert.ok(sale.folio); assert.ok(sale.createdAt);
    assert.equal(sale.items[0].quantity, 2); assert.equal(sale.items[0].priceCents, 2500);
    assert.equal((await api('/api/products')).find(p => p.id === product.id).stock, 8); checks.push('sale-ticket-change-stock');
    const duplicate = await api('/api/sales', input, 201);
    assert.equal(duplicate.id, sale.id); assert.equal((await api('/api/sales')).length, 1);
    assert.equal((await api('/api/products')).find(p => p.id === product.id).stock, 8); checks.push('idempotency');
    await stop(); base = await start();
    assert.equal((await api('/api/sales'))[0].id, sale.id);
    assert.equal((await api('/api/products')).find(p => p.id === product.id).stock, 8); checks.push('restart-persistence');
    if (browser) {
      const require = createRequire(import.meta.url);
      const { chromium } = require('/usr/local/lib/node_modules/playwright');
      const instance = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
      try {
        const page = await instance.newPage();
        await page.goto(base);
        await page.locator('[data-testid="product"]').selectOption(String(product.id));
        await page.locator('[data-testid="quantity"]').fill('1');
        await page.locator('[data-testid="paid"]').fill('30');
        await page.locator('[data-testid="sell"]').click();
        await page.locator('[data-testid="ticket"]').filter({ hasText: '500' }).waitFor();
        assert.equal((await api('/api/products')).find(p => p.id === product.id).stock, 7);
        assert.equal((await api('/api/sales')).length, 2); checks.push('browser-sale');
      } finally { await instance.close(); }
    }
    const instructions = await readFile(join(work, 'README.md'), 'utf8');
    assert.ok(instructions.includes('npm') && instructions.includes('POS_DB')); checks.push('delivery-instructions');
    return { passed: true, checks, browserVerified: browser };
  } finally { await stop(); }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  runOracle(process.argv[2], '/project').then(result => console.log(JSON.stringify(result))).catch(error => { console.error(error.message); process.exitCode = 1; });
}
