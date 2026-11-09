# Access boundary check — local PoC report

- Target: http://127.0.0.1:4321
- Generated: 2026-11-09T06:58:06.746Z
- Summary: 4 pass · 1 review · 0 inconclusive
- Scope: GET-only checks against a loopback fixture. HTTP status classification is a heuristic, not proof of security.

## PASS: 사용자는 자기 조직 문서를 읽을 수 있음

- Identity: Acme 일반 사용자
- Request: GET /api/documents/acme-plan
- Expected: allow
- Actual: allow (HTTP 200)
- Response evidence: `Response body: 102 bytes captured; expected marker: matched; forbidden marker matches: 0. Raw response was not saved.`

## PASS: 다른 조직 문서 접근은 차단되어야 함

- Identity: Acme 일반 사용자
- Request: GET /api/documents/birch-roadmap
- Expected: deny
- Actual: deny (HTTP 404)
- Response evidence: `Response body: 21 bytes captured; expected marker: not configured; forbidden marker matches: 0. Raw response was not saved.`

## PASS: 문서 소유 조직 사용자는 문서를 읽을 수 있음

- Identity: Birch 일반 사용자
- Request: GET /api/documents/birch-roadmap
- Expected: allow
- Actual: allow (HTTP 200)
- Response evidence: `Response body: 110 bytes captured; expected marker: matched; forbidden marker matches: 0. Raw response was not saved.`

## REVIEW: 일반 사용자는 관리자 감사 로그를 읽을 수 없어야 함

- Identity: Acme 일반 사용자
- Request: GET /api/documents/acme-audit-log
- Expected: deny
- Actual: allow (HTTP 200)
- Response evidence: `Response body: 129 bytes captured; expected marker: not configured; forbidden marker matches: 2. Raw response was not saved.`

## PASS: 관리자는 자기 조직 감사 로그를 읽을 수 있음

- Identity: Acme 관리자
- Request: GET /api/documents/acme-audit-log
- Expected: allow
- Actual: allow (HTTP 200)
- Response evidence: `Response body: 129 bytes captured; expected marker: matched; forbidden marker matches: 0. Raw response was not saved.`

A pass means the observed response matched the declared expectation for this one request. It does not certify that the application is secure.
