import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import process from 'node:process';
import { createScenarioCollector, normalizePrefix as sharedNormalizePrefix, option as sharedOption, parseOrigin as sharedParseOrigin, redactPath } from './scenario-draft-utils.mjs';

const inputName = sharedOption(process.argv, 'file');
const origin = sharedParseOrigin(sharedOption(process.argv, 'origin'));
const prefix = sharedNormalizePrefix(sharedOption(process.argv, 'prefix'));
if (!inputName) throw new Error('Supply a HAR file with --file=path-to-capture.har.');

const inputPath = resolve(inputName);
const bytes = await readFile(inputPath);
if (bytes.byteLength > 10 * 1024 * 1024) throw new Error('HAR input exceeds the 10 MiB PoC limit.');

let har;
try {
  har = JSON.parse(bytes.toString('utf8'));
} catch {
  throw new Error('HAR input is not valid JSON.');
}
const entries = har?.log?.entries;
if (!Array.isArray(entries)) throw new Error('HAR input must contain log.entries.');
if (entries.length > 5000) throw new Error('HAR input exceeds the 5,000-entry PoC limit.');

const checks = createScenarioCollector('captured');
let originMatches = 0;
let prefixMatches = 0;
let unsupportedMethods = 0;
let badUrls = 0;

for (const entry of entries) {
  const request = entry?.request;
  if (!request || typeof request.url !== 'string' || typeof request.method !== 'string') continue;
  let requestUrl;
  try {
    requestUrl = new URL(request.url);
  } catch {
    badUrls += 1;
    continue;
  }
  if (requestUrl.origin !== origin) continue;
  originMatches += 1;
  if (!requestUrl.pathname.startsWith(prefix)) continue;
  prefixMatches += 1;

  const method = request.method.toUpperCase();
  if (!['GET', 'PATCH'].includes(method)) {
    unsupportedMethods += 1;
    continue;
  }

  checks.add({ method, path: redactPath(requestUrl.pathname) });
}

const draft = {
  schema: 'access-boundary-scenario-draft/v1',
  status: 'draft-not-runnable',
  authorized: false,
  allow_writes: false,
  instructions: [
    'Assign each request to a dedicated test identity and set an explicit expected allow/deny rule before use.',
    'Review every redacted path and replace placeholders only with disposable fixture identifiers.',
    'PATCH entries have no request body; create a harmless fixture-only body manually before any separately authorized write test.',
    'This draft has no captured headers, cookies, query strings, request bodies, response bodies, or response headers.',
  ],
  import_summary: {
    format: 'HAR',
    selected_origin: origin,
    path_prefix: prefix,
    total_entries: entries.length,
    matching_origin_entries: originMatches,
    matching_prefix_entries: prefixMatches,
    unsupported_methods_skipped: unsupportedMethods,
    invalid_urls_skipped: badUrls,
    unique_request_templates: checks.size,
  },
  identities: {},
  checks: checks.values(),
};

const output = `${JSON.stringify(draft, null, 2)}\n`;
const outputName = sharedOption(process.argv, 'out') ?? 'poc/har-scenario-draft.json';
if (outputName === '-') process.stdout.write(output);
else {
  const outputPath = resolve(outputName);
  await writeFile(outputPath, output, { encoding: 'utf8', flag: 'wx' });
  process.stdout.write(`Sanitized draft written to ${outputPath}\n`);
}
