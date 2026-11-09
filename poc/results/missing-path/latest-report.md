# Access boundary check — local PoC report

- Target: http://127.0.0.1:4320
- Generated: 2026-11-09T02:59:26.036Z
- Summary: 1 pass · 1 review · 1 inconclusive
- Scope: GET-only checks against a loopback fixture. HTTP status classification is a heuristic, not proof of security.

## PASS: 사용자는 자기 조직 문서를 읽을 수 있음

- Identity: Acme 일반 사용자
- Request: GET /api/documents/acme-plan
- Expected: allow
- Actual: allow (HTTP 200)
- Response evidence: `Response body: 102 bytes captured; expected marker: matched; forbidden marker matches: 0. Raw response was not saved.`

## INCONCLUSIVE: 다른 조직 문서 접근은 차단되어야 함

- Identity: Acme 일반 사용자
- Request: GET /api/documents/no-such-document
- Expected: deny
- Actual: deny (HTTP 404)
- Note: The deny response is not counted as a pass because a different identity was not confirmed to read the same resource (control: other-owner-can-read).
- Response evidence: `Response body: 21 bytes captured; expected marker: not configured; forbidden marker matches: 0. Raw response was not saved.`

## REVIEW: 문서 소유 조직 사용자는 문서를 읽을 수 있음

- Identity: Birch 일반 사용자
- Request: GET /api/documents/no-such-document
- Expected: allow
- Actual: deny (HTTP 404)
- Response evidence: `Response body: 21 bytes captured; expected marker: not matched; forbidden marker matches: 0. Raw response was not saved.`

A pass means the observed response matched the declared expectation for this one request. It does not certify that the application is secure.
