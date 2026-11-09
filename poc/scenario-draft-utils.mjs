export function option(args, name) {
  const prefix = `--${name}=`;
  return args.find((value) => value.startsWith(prefix))?.slice(prefix.length);
}

export function parseOrigin(value) {
  if (!value) throw new Error('Supply one authorized app origin with --origin=https://app.example.');
  let url;
  try { url = new URL(value); } catch { throw new Error('The selected origin is not a valid URL.'); }
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.pathname !== '/' || url.search || url.hash || value.replace(/\/$/, '') !== url.origin) {
    throw new Error('Origin must be exactly an HTTP(S) origin, with no credentials, path, query, or fragment.');
  }
  return url.origin;
}

export function normalizePrefix(value) {
  if (!value) throw new Error('Supply an API path prefix with --prefix=/api/.');
  if (!value.startsWith('/') || value.startsWith('//') || value.includes('\\') || value.includes('?') || value.includes('#')) {
    throw new Error('Path prefix must be an absolute path prefix without query, fragment, backslash, or parent traversal.');
  }
  for (const segment of value.split('/')) {
    let decoded;
    try { decoded = decodeURIComponent(segment); } catch { throw new Error('Path prefix contains invalid percent encoding.'); }
    if (decoded === '.' || decoded === '..' || decoded.includes('/') || decoded.includes('\\') || /[\u0000-\u001f\u007f]/.test(decoded)) {
      throw new Error('Path prefix must not contain traversal or encoded path separators.');
    }
  }
  return value.endsWith('/') ? value : `${value}/`;
}

export function redactPath(pathname) {
  const normalized = pathname.replace(/\{[^{}]+\}/g, '{redacted}');
  return normalized.split('/').map((segment) => {
    if (!segment) return '';
    let decoded;
    try { decoded = decodeURIComponent(segment); } catch { return '{redacted}'; }
    if (decoded === '{redacted}') return decoded;
    const sensitive = decoded.length > 80 || /[@\\/]/.test(decoded)
      || /^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(decoded) || /^\d{5,}$/.test(decoded)
      || /^[A-Za-z0-9_-]{28,}$/.test(decoded) || /^(?:bearer|token|secret|key)[-_:.]/i.test(decoded)
      || decoded === '.' || decoded === '..' || /[\u0000-\u001f\u007f]/.test(decoded);
    return sensitive || !/^[A-Za-z0-9._~-]+$/.test(decoded) ? '{redacted}' : decoded;
  }).join('/') || '/';
}

export function createScenarioCollector(idPrefix) {
  const checks = new Map();
  return {
    add({ method, path, occurrences = 1 }) {
      const key = `${method} ${path}`;
      const existing = checks.get(key);
      if (existing) { existing.occurrences = (existing.occurrences ?? 1) + occurrences; return; }
      checks.set(key, {
        id: `${idPrefix}-${String(checks.size + 1).padStart(3, '0')}`,
        title: `검토 필요: ${method} ${path}`,
        identity: null, method, path, suggested_expectation: null,
        needs_human_review: true,
        ...(occurrences !== 1 ? { occurrences } : {}),
        ...(method === 'PATCH' ? { requires_safe_test_body: true } : {}),
        ...(path.includes('{redacted}') ? { path_contains_redactions: true } : {}),
      });
    },
    values() { return [...checks.values()]; },
    get size() { return checks.size; },
  };
}
