# HAR importer — 로컬 privacy check

합성 HAR 5개 항목을 검사했다. 선택 origin과 `/api/` 경로 접두사만 골라 중복 요청을 합쳤고, 결과는 다음과 같다.

- 선택 origin 일치: 4개
- 선택 경로 접두사 일치: 3개
- 고유 템플릿: GET 1개, PATCH 1개
- 동적 숫자 ID: `{redacted}`로 출력
- fake Authorization/Cookie 값, 쿼리 문자열, 요청 본문, 응답 본문: 초안에 미포함
- 산출물: `draft-not-runnable`, `authorized: false`, `allow_writes: false`

이 확인은 비밀 필드 제거와 초안 생성만 검증한다. 실제 고객 HAR를 지원하거나 개인정보 경로를 완벽히 찾아낸다는 뜻은 아니다. 모든 경로를 사람이 검토해야 하며, HAR 원본은 민감한 파일로 취급해야 한다.
