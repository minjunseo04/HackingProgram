# 제한된 쓰기 권한 검사 — 로컬 fixture 결과

## 결과

| 실행 모드 | 통과 | 검토 | 확인 불가 | 의미 |
|---|---:|---:|---:|---|
| 안전 fixture + 명시적 PATCH 허용 | 2 | 0 | 0 | Acme 사용자의 Birch 문서 변경은 차단됐고, 소유자는 원래 제목을 읽음 |
| `--write-vulnerable` + 명시적 PATCH 허용 | 0 | 1 | 1 | 무단 제목 변경을 검토로 잡았고, 후속 소유자 확인에서 원래 제목이 없어 확인 불가 처리 |
| 시나리오 opt-in만 있고 CLI `--allow-local-writes` 없음 | 1 | 0 | 1 | PATCH 요청을 보내지 않고 건너뜀. 소유자 GET은 원래 제목을 확인 |

## 범위와 한계

- `127.0.0.1` fixture의 `PATCH /api/documents/:id`만 실행
- 문서 제목만 프로세스 메모리에서 바꿈. 서버를 재시작하면 초기 샘플 데이터로 복구
- 쓰기 요청은 loopback URL, 시나리오의 `allow_writes: true`, CLI의 `--allow-local-writes`, fixture의 `--enable-test-writes`가 모두 있어야 보냄
- 실제 서비스, 실제 자격 증명, 삭제 작업은 사용하지 않음
- 결과는 고정된 예제의 동작 확인이며 범용 권한 테스트의 정확도를 입증하지 않음
