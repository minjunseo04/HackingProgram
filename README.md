# 권한 경계 검증 도구

앱 권한 경계 점검 프로그램과 소개 웹사이트를 개발하는 프로젝트입니다. 웹사이트는 제품 설명과 다운로드만 제공하며, 프로젝트 작업은 Windows 데스크톱 프로그램 안에서 진행합니다.

## 프로젝트 기준

- 제품 방향은 Supabase 전용이 아닙니다. Supabase 예제는 초기 실험물로 분리되어 있습니다.
- 초기 문제 범위는 다중 사용자·역할·조직 사이의 접근 경계입니다(IDOR/BOLA, 역할 상승, 테넌트 간 접근).
- PoC는 `127.0.0.1`의 샘플 API만 다룹니다. 외부 호스트를 검사하지 않습니다.
- 기본 실행은 읽기 요청만 허용합니다. PATCH는 loopback fixture에서 여러 명시적 허용 단계가 있어야 전송합니다.
- 프로그램 v0.1.0은 HAR/OpenAPI JSON 가져오기, 요청 초안 정리, 기대 접근값 편집과 로컬 저장을 지원합니다. 실제 요청 실행은 아직 포함하지 않습니다.
- AI 페르소나 검토는 가설 점검이지 고객 수요나 지불 의사 검증이 아닙니다. 제작자·학생 사용자 테스트와 사업성 검증은 구분합니다.

## 현재 구성

- `docs/project-report.md` — 사업 가설 및 페르소나 검토
- `docs/project-status.md` — 결정 사항, 현재 한계, 다음 작업
- `docs/roadmap.md` — 단계별 진행 계획
- `desktop/` — Windows 데스크톱 프로그램 소스
- `website/` — 설명과 다운로드 링크만 있는 정적 웹사이트
- `demo/index.html` — 기존 제품 흐름 시뮬레이션(실제 기능 아님)
- `experiments/supabase-policy-prototype/` — 초기 Supabase 전용 실험물
- `poc/` — 로컬 샘플 API와 다중 신원 권한 검사기

실행 방법과 한계는 [`poc/README.md`](poc/README.md)를 참고하세요.

데스크톱 프로그램은 .NET 10 Windows Desktop SDK로 빌드합니다. 웹사이트 파일은 `website/index.html`입니다. 배포용 ZIP은 `powershell -ExecutionPolicy Bypass -File .\desktop\publish.ps1`로 생성하며, 빌드 산출물은 Git 이력에 넣지 않습니다.

로컬 Windows 패키지는 [`website/downloads/BoundaryCheck-win-x64.zip`](website/downloads/BoundaryCheck-win-x64.zip)입니다. 웹사이트를 배포할 때는 이 ZIP도 다운로드 위치에 함께 올려야 합니다.
