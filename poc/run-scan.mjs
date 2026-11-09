import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import process from 'node:process';

function argument(name, fallback) {
  const prefix = `--${name}=`;
  const found = process.argv.find((value) => value.startsWith(prefix));
  return found ? found.slice(prefix.length) : fallback;
}

function localBaseUrl(value) {
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error('Base URL is invalid.');
  }
  const allowedHosts = new Set(['127.0.0.1', '[::1]']);
  if (!allowedHosts.has(url.hostname)) {
    throw new Error('Safety stop: this PoC only sends requests to literal loopback IP addresses (127.0.0.1 or ::1).');
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error('Base URL must use HTTP or HTTPS.');
  }
  if (url.username || url.password || url.search || url.hash || url.pathname !== '/') {
    throw new Error('Base URL must contain only the loopback origin, without credentials, path, query, or fragment.');
  }
  return url;
}

function checkPath(path) {
  if (typeof path !== 'string' || !path.startsWith('/') || path.startsWith('//') || path.includes('\\')) {
    throw new Error(`Unsafe request path: ${String(path)}`);
  }
  return path;
}

function classify(status) {
  if (status >= 200 && status < 300) return 'allow';
  if ([401, 403, 404].includes(status)) return 'deny';
  return 'inconclusive';
}

function resolveIdentityHeaders(identity, identityId) {
  if (!identity.headers || typeof identity.headers !== 'object' || Array.isArray(identity.headers)) {
    throw new Error(`Identity ${identityId} must define a headers object.`);
  }

  const blockedHeaders = new Set(['host', 'connection', 'content-length', 'transfer-encoding', 'upgrade', 'proxy-authorization']);
  const entries = Object.entries(identity.headers).map(([name, configuredValue]) => {
    if (blockedHeaders.has(name.toLowerCase())) throw new Error(`Header ${name} is not allowed in identity configuration.`);
    let value = configuredValue;
    if (configuredValue && typeof configuredValue === 'object' && typeof configuredValue.env === 'string') {
      value = process.env[configuredValue.env];
      if (!value) throw new Error(`Environment variable ${configuredValue.env} is not set.`);
    }
    if (typeof value !== 'string' || /[\r\n]/.test(value)) throw new Error(`Header ${name} must resolve to a single-line string.`);
    return [name, value];
  });

  try {
    return Object.fromEntries(new Headers(entries).entries());
  } catch {
    throw new Error(`Identity ${identityId} contains an invalid HTTP header.`);
  }
}

async function readBoundedBody(response, maxBytes = 64 * 1024) {
  if (!response.body) return { text: '', bytes: 0, truncated: false };
  const reader = response.body.getReader();
  const chunks = [];
  let bytes = 0;
  let truncated = false;

  while (true) {
    const { value, done } = await reader.read();
    if (done) break;
    const remaining = maxBytes - bytes;
    if (value.byteLength > remaining) {
      if (remaining > 0) {
        chunks.push(value.subarray(0, remaining));
        bytes += remaining;
      }
      truncated = true;
      await reader.cancel();
      break;
    }
    chunks.push(value);
    bytes += value.byteLength;
  }

  const body = new Uint8Array(bytes);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return { text: new TextDecoder().decode(body), bytes, truncated };
}

const configPath = resolve(argument('config', 'poc/scenarios.json'));
const baseUrl = localBaseUrl(argument('base-url', 'http://127.0.0.1:4318'));
const config = JSON.parse(await readFile(configPath, 'utf8'));
const allowLocalWrites = process.argv.includes('--allow-local-writes');

if (config.authorized !== true) {
  throw new Error('Safety stop: set "authorized": true after confirming you own or are allowed to test this local fixture.');
}
if (!Array.isArray(config.checks) || !config.identities || typeof config.identities !== 'object') {
  throw new Error('Config must define identities and a checks array.');
}
for (const check of config.checks) {
  if (!['allow', 'deny'].includes(check.expected)) {
    throw new Error(`Check ${check.id ?? '(unnamed)'} must set expected to "allow" or "deny".`);
  }
}

const results = [];
for (const check of config.checks) {
  if (!['GET', 'PATCH'].includes(check.method)) {
    results.push({ id: check.id, title: check.title, verdict: 'inconclusive', note: 'This PoC only permits GET and controlled PATCH requests.' });
    continue;
  }
  if (check.method === 'PATCH' && (!allowLocalWrites || config.allow_writes !== true)) {
    results.push({ id: check.id, title: check.title, verdict: 'inconclusive', note: 'PATCH skipped. Both allow_writes: true in the scenario file and --allow-local-writes are required.' });
    continue;
  }
  if (check.method === 'PATCH' && (!/^\/api\/documents\/[a-z-]+$/.test(check.path) || !check.body || typeof check.body !== 'object' || Array.isArray(check.body))) {
    results.push({ id: check.id, title: check.title, verdict: 'inconclusive', note: 'PATCH is limited to a fixture document path with a JSON object body.' });
    continue;
  }

  const identity = config.identities[check.identity];
  if (!identity?.headers) {
    results.push({ id: check.id, title: check.title, verdict: 'inconclusive', note: `Unknown identity: ${check.identity}` });
    continue;
  }

  try {
    const url = new URL(checkPath(check.path), baseUrl);
    if (url.origin !== baseUrl.origin) throw new Error('Request path changed the configured origin.');

    const headers = { ...resolveIdentityHeaders(identity, check.identity), Accept: 'application/json' };
    const requestBody = check.method === 'PATCH' ? JSON.stringify(check.body) : undefined;
    if (requestBody && Buffer.byteLength(requestBody, 'utf8') > 4096) throw new Error('PATCH request body exceeds 4 KiB.');
    if (requestBody) headers['Content-Type'] = 'application/json';

    const response = await fetch(url, {
      method: check.method,
      headers,
      body: requestBody,
      redirect: 'manual',
      signal: AbortSignal.timeout(5000),
    });
    const body = await readBoundedBody(response);
    const forbiddenMarkers = Array.isArray(check.must_not_contain) ? check.must_not_contain.filter((marker) => typeof marker === 'string' && marker.length > 0) : [];
    const matchedForbiddenMarkers = forbiddenMarkers.filter((marker) => body.text.includes(marker));
    let actual = classify(response.status);
    if (matchedForbiddenMarkers.length > 0) actual = 'allow';
    if (body.truncated && actual !== 'allow') actual = 'inconclusive';
    if (check.expected === 'allow' && check.expected_contains && actual === 'allow' && !body.text.includes(check.expected_contains)) {
      actual = 'inconclusive';
    }
    const expected = check.expected;
    const verdict = actual === 'inconclusive' ? 'inconclusive' : actual === expected ? 'pass' : 'review';
    results.push({
      id: check.id,
      title: check.title,
      identity: identity.label ?? check.identity,
      method: check.method,
      path: check.path,
      expected,
      actual,
      status: response.status,
      verdict,
      evidence: `Response body: ${body.bytes} bytes captured${body.truncated ? ' (truncated at 64 KiB)' : ''}; expected marker: ${check.expected_contains ? (body.text.includes(check.expected_contains) ? 'matched' : 'not matched') : 'not configured'}; forbidden marker matches: ${matchedForbiddenMarkers.length}. Raw response was not saved.`,
    });
  } catch (error) {
    const cause = error.cause?.message ? ` (${error.cause.message})` : '';
    results.push({ id: check.id, title: check.title, verdict: 'inconclusive', note: `${error.message}${cause}` });
  }
}

const resultsById = new Map(results.map((result) => [result.id, result]));
for (const check of config.checks.filter((item) => item.expected === 'deny')) {
  const result = resultsById.get(check.id);
  if (!result || result.actual !== 'deny' || !result.identity) continue;

  const controlCheck = config.checks.find((item) => item.id === check.exists_check);
  const control = resultsById.get(check.exists_check);
  const controlMatchesResource = controlCheck
    && controlCheck.method === 'GET'
    && controlCheck.path === check.path
    && controlCheck.identity !== check.identity;
  if (!controlMatchesResource || !control || control.actual !== 'allow' || control.verdict !== 'pass') {
    result.verdict = 'inconclusive';
    result.note = `The deny response is not counted as a pass because a different identity was not confirmed to read the same resource (control: ${check.exists_check ?? 'missing'}).`;
  }
}

const summary = {
  title: 'Access boundary check — local PoC report',
  generatedAt: new Date().toISOString(),
  target: baseUrl.origin,
  scope: `${[...new Set(config.checks.map((check) => check.method))].join(', ')} checks against a loopback fixture. PATCH requires the fixture flag, scenario flag, and CLI flag. HTTP status and body markers are heuristics, not proof of security.`,
  counts: {
    pass: results.filter((item) => item.verdict === 'pass').length,
    review: results.filter((item) => item.verdict === 'review').length,
    inconclusive: results.filter((item) => item.verdict === 'inconclusive').length,
  },
  results,
};

const markdown = [
  '# Access boundary check — local PoC report',
  '',
  `- Target: ${summary.target}`,
  `- Generated: ${summary.generatedAt}`,
  `- Summary: ${summary.counts.pass} pass · ${summary.counts.review} review · ${summary.counts.inconclusive} inconclusive`,
  `- Scope: ${summary.scope}`,
  '',
  ...results.flatMap((item) => [
    `## ${item.verdict.toUpperCase()}: ${item.title ?? item.id}`,
    '',
    item.identity ? `- Identity: ${item.identity}` : null,
    item.method ? `- Request: ${item.method} ${item.path}` : null,
    item.expected ? `- Expected: ${item.expected}` : null,
    item.actual ? `- Actual: ${item.actual} (HTTP ${item.status})` : null,
    item.note ? `- Note: ${item.note}` : null,
    item.evidence ? `- Response evidence: \`${item.evidence.replaceAll('`', "'")}\`` : null,
    '',
  ].filter((line) => line !== null)),
  'A pass means the observed response matched the declared expectation for this one request. It does not certify that the application is secure.',
].join('\n');

const outputArgument = argument('out', 'poc/results');
if (outputArgument === '-') {
  process.stdout.write(`${markdown}\n`);
} else {
  const outputDirectory = resolve(outputArgument);
  await mkdir(outputDirectory, { recursive: true });
  await writeFile(resolve(outputDirectory, 'latest-report.json'), `${JSON.stringify(summary, null, 2)}\n`, 'utf8');
  await writeFile(resolve(outputDirectory, 'latest-report.md'), `${markdown}\n`, 'utf8');
  process.stdout.write(`Report: ${resolve(outputDirectory, 'latest-report.md')}\n`);
}

process.stdout.write(`Checks: ${summary.counts.pass} pass, ${summary.counts.review} review, ${summary.counts.inconclusive} inconclusive\n`);
if (summary.counts.review > 0) process.exitCode = 1;
else if (summary.counts.inconclusive > 0) process.exitCode = 2;
