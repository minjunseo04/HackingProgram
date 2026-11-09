import http from 'node:http';

const args = new Set(process.argv.slice(2));
const vulnerable = args.has('--vulnerable');
const deniedButLeaks = args.has('--deny-status-but-leaks');
const roleVulnerable = args.has('--role-vulnerable');
const roleWriteVulnerable = args.has('--role-write-vulnerable');
const writeVulnerable = args.has('--write-vulnerable');
const testWritesEnabled = args.has('--enable-test-writes');
const portArg = process.argv.find((value) => value.startsWith('--port='));
const port = Number(portArg?.split('=')[1] ?? 4318);

const identities = new Map([
  ['sample-acme-token', { id: 'acme-user', tenant: 'acme', role: 'member' }],
  ['sample-acme-admin-token', { id: 'acme-admin', tenant: 'acme', role: 'admin' }],
  ['sample-birch-token', { id: 'birch-user', tenant: 'birch', role: 'member' }],
]);

const documents = new Map([
  ['acme-plan', { id: 'acme-plan', tenant: 'acme', title: 'Acme 출시 계획', body: 'Acme 조직의 내부 계획 문서입니다.' }],
  ['birch-roadmap', { id: 'birch-roadmap', tenant: 'birch', title: 'Birch 제품 로드맵', body: 'Birch 조직의 비공개 로드맵입니다.' }],
  ['acme-audit-log', { id: 'acme-audit-log', tenant: 'acme', requiredRole: 'admin', title: 'Acme 운영자 감사 로그', body: '운영자만 확인할 수 있는 감사 기록입니다.' }],
]);

const server = http.createServer(async (request, response) => {
  response.setHeader('Content-Type', 'application/json; charset=utf-8');
  response.setHeader('Cache-Control', 'no-store');

  if (!['GET', 'PATCH'].includes(request.method ?? '') || (request.method === 'PATCH' && !testWritesEnabled)) {
    response.writeHead(405, { Allow: 'GET, PATCH' });
    response.end(JSON.stringify({ error: 'Only GET and controlled PATCH are supported in this fixture.' }));
    return;
  }

  const url = new URL(request.url ?? '/', 'http://127.0.0.1');
  if (url.pathname === '/health') {
    response.writeHead(200);
    response.end(JSON.stringify({ ok: true, mode: vulnerable ? 'vulnerable-example' : 'secure-example' }));
    return;
  }

  const match = url.pathname.match(/^\/api\/documents\/([a-z-]+)$/);
  if (!match) {
    response.writeHead(404);
    response.end(JSON.stringify({ error: 'Not found' }));
    return;
  }

  const bearerToken = request.headers.authorization?.replace(/^Bearer\s+/i, '');
  const cookieToken = request.headers.cookie?.split(';').map((part) => part.trim()).find((part) => part.startsWith('session='))?.slice('session='.length);
  const apiKey = request.headers['x-api-key'];
  const token = bearerToken ?? cookieToken ?? apiKey;
  const identity = identities.get(token ?? '');
  if (!identity) {
    response.writeHead(401);
    response.end(JSON.stringify({ error: 'Authentication required' }));
    return;
  }

  const document = documents.get(match[1]);
  if (!document) {
    response.writeHead(404);
    response.end(JSON.stringify({ error: 'Not found' }));
    return;
  }

  // The vulnerable mode intentionally omits the tenant ownership check.
  const bypassWriteAuthorization = request.method === 'PATCH' && writeVulnerable;
  if (!vulnerable && !bypassWriteAuthorization && document.tenant !== identity.tenant) {
    if (deniedButLeaks) {
      response.writeHead(403);
      response.end(JSON.stringify({ error: 'Forbidden', leakedTitle: document.title, leakedBody: document.body }));
      return;
    }
    response.writeHead(404);
    response.end(JSON.stringify({ error: 'Not found' }));
    return;
  }

  const bypassRoleWriteCheck = request.method === 'PATCH' && roleWriteVulnerable;
  if (!roleVulnerable && !bypassWriteAuthorization && !bypassRoleWriteCheck && document.requiredRole && identity.role !== document.requiredRole) {
    response.writeHead(404);
    response.end(JSON.stringify({ error: 'Not found' }));
    return;
  }

  if (request.method === 'PATCH') {
    let rawBody = '';
    for await (const chunk of request) {
      rawBody += chunk.toString('utf8');
      if (Buffer.byteLength(rawBody, 'utf8') > 4096) {
        response.writeHead(413);
        response.end(JSON.stringify({ error: 'Fixture accepts request bodies up to 4 KiB.' }));
        return;
      }
    }

    let payload;
    try {
      payload = JSON.parse(rawBody);
    } catch {
      response.writeHead(400);
      response.end(JSON.stringify({ error: 'A JSON body is required.' }));
      return;
    }
    if (!payload || typeof payload !== 'object' || Array.isArray(payload) || Object.keys(payload).some((key) => key !== 'title') || typeof payload.title !== 'string' || payload.title.length > 100) {
      response.writeHead(400);
      response.end(JSON.stringify({ error: 'Only a title string of at most 100 characters can be changed.' }));
      return;
    }
    document.title = payload.title;
  }

  response.writeHead(200);
  response.end(JSON.stringify({ id: document.id, title: document.title, body: document.body }));
});

server.listen(port, '127.0.0.1', () => {
  const modes = [
    vulnerable ? 'tenant-vulnerable' : 'tenant-isolated',
    roleVulnerable ? 'role-vulnerable' : 'role-checked',
    roleWriteVulnerable ? 'role-write-vulnerable' : null,
    testWritesEnabled ? 'fixture-writes-enabled' : 'read-only',
    writeVulnerable ? 'write-vulnerable' : 'write-checked',
    deniedButLeaks ? 'denial-status-with-sensitive-body' : null,
  ].filter(Boolean).join(' + ');
  process.stdout.write(`Local fixture listening at http://127.0.0.1:${port} (${modes})\n`);
  process.stdout.write('This fixture binds to loopback only. PATCH changes only in-memory fixture titles; restart to reset.\n');
});

server.on('error', (error) => {
  process.stderr.write(`Could not start local fixture: ${error.message}\n`);
  process.exitCode = 1;
});
