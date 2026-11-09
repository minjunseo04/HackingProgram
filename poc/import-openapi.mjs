import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import process from 'node:process';
import { createScenarioCollector, normalizePrefix as sharedNormalizePrefix, option as sharedOption, parseOrigin as sharedParseOrigin, redactPath as sharedRedactPath } from './scenario-draft-utils.mjs';

function parseServerUrl(value, origin) {
  if (!value) return '/';
  let url;
  try {
    url = new URL(value);
  } catch {
    throw new Error('--server-url must be an absolute HTTP(S) URL.');
  }
  if (!['http:', 'https:'].includes(url.protocol) || url.origin !== origin || url.username || url.password || url.search || url.hash || /[{}]/.test(url.pathname) || url.pathname.split('/').includes('..')) {
    throw new Error('--server-url must use the selected origin and contain a fixed path without credentials, variables, query, or fragment.');
  }
  return url.pathname.replace(/\/$/, '') || '/';
}

const inputName = sharedOption(process.argv, 'file');
const origin = sharedParseOrigin(sharedOption(process.argv, 'origin'));
const prefix = sharedNormalizePrefix(sharedOption(process.argv, 'prefix'));
const serverPath = parseServerUrl(sharedOption(process.argv, 'server-url'), origin);
if (!inputName) throw new Error('Supply an OpenAPI JSON file with --file=path-to-openapi.json.');

const bytes = await readFile(resolve(inputName));
if (bytes.byteLength > 10 * 1024 * 1024) throw new Error('OpenAPI input exceeds the 10 MiB PoC limit.');
let spec;
try {
  spec = JSON.parse(bytes.toString('utf8'));
} catch {
  throw new Error('OpenAPI input must be JSON. YAML is not supported by this PoC.');
}
if (!/^3\.(0|1)\./.test(spec?.openapi ?? '') || !spec.paths || typeof spec.paths !== 'object' || Array.isArray(spec.paths)) {
  throw new Error('Input must be an OpenAPI 3.0 or 3.1 JSON document with a paths object.');
}

const checks = createScenarioCollector('openapi');
let pathCount = 0;
let matchingPrefixOperations = 0;
let unsupportedMethods = 0;
let invalidPaths = 0;
const acceptedMethods = new Set(['get', 'patch']);

for (const [specPath, pathItem] of Object.entries(spec.paths)) {
  if (!specPath.startsWith('/') || specPath.startsWith('//') || specPath.includes('\\') || specPath.includes('?') || specPath.includes('#') || specPath.split('/').includes('..')) {
    invalidPaths += 1;
    continue;
  }
  pathCount += 1;
  const combinedPath = `${serverPath === '/' ? '' : serverPath}${specPath}`;
  if (!combinedPath.startsWith(prefix)) continue;

  for (const method of Object.keys(pathItem ?? {})) {
    if (['parameters', 'servers', 'summary', 'description', '$ref'].includes(method)) continue;
    const lowerMethod = method.toLowerCase();
    if (!acceptedMethods.has(lowerMethod)) {
      unsupportedMethods += 1;
      continue;
    }
    matchingPrefixOperations += 1;
    const verb = lowerMethod.toUpperCase();
    checks.add({ method: verb, path: sharedRedactPath(combinedPath) });
  }
}

const draft = {
  schema: 'access-boundary-scenario-draft/v1',
  status: 'draft-not-runnable',
  authorized: false,
  allow_writes: false,
  instructions: [
    'Assign a dedicated test identity and set an explicit expected allow/deny rule for every operation.',
    'Review redacted path segments and replace placeholders only with disposable fixture identifiers.',
    'PATCH operations contain no request body; define a harmless fixture-only body manually before any write check.',
    'The importer does not include descriptions, examples, schemas, security definitions, headers, or credentials.',
  ],
  import_summary: {
    format: 'OpenAPI JSON',
    version: spec.openapi,
    selected_origin: origin,
    selected_server_base_path: serverPath,
    path_prefix: prefix,
    declared_paths: pathCount,
    matching_prefix_operations: matchingPrefixOperations,
    unsupported_methods_skipped: unsupportedMethods,
    invalid_paths_skipped: invalidPaths,
    unique_request_templates: checks.size,
  },
  identities: {},
  checks: checks.values(),
};

const output = `${JSON.stringify(draft, null, 2)}\n`;
const outputName = sharedOption(process.argv, 'out') ?? 'poc/openapi-scenario-draft.json';
if (outputName === '-') process.stdout.write(output);
else {
  const outputPath = resolve(outputName);
  await writeFile(outputPath, output, { encoding: 'utf8', flag: 'wx' });
  process.stdout.write(`Sanitized draft written to ${outputPath}\n`);
}
