# Local PoC

작은 기술 실험입니다. 구성 파일에 선언한 GET 요청을 서로 다른 테스트 계정으로 실행하고, 세 겹의 opt-in이 켜진 경우 fixture 문서 제목 PATCH 한 종류만 비교합니다. 기본 샘플 API는 `127.0.0.1`에만 바인딩됩니다.

## 필요한 것

- Node.js 20 이상 권장
- 추가 패키지 설치 불필요

## HAR에서 시나리오 초안 만들기

HAR를 읽어 선택한 origin과 경로 접두사에 해당하는 GET/PATCH 요청 템플릿만 추출합니다. 이 도구는 HAR의 URL에 요청을 보내지 않습니다.
두 importer는 `scenario-draft-utils.mjs`의 공통 origin·접두사 검증, 경로 가림, method/path 중복 제거를 사용합니다. 경로 접두사에는 인코딩된 상위 경로나 경로 구분자를 넣을 수 없습니다.

```powershell
node .\poc\import-har.mjs --file=.\poc\sample.har --origin=https://app.example.test --prefix=/api/ --out=-
```

파일로 저장하려면 `--out=.\poc\har-scenario-draft.json`처럼 지정합니다. 기존 파일을 덮어쓰지 않습니다.

생성물은 `draft-not-runnable` 상태입니다. 인증 신원, 권한 기대값, 안전한 PATCH 본문은 자동으로 추측하지 않습니다. 쿼리 문자열, 모든 요청/응답 헤더, 쿠키, 요청/응답 본문은 출력에서 제외하고 UUID·긴 숫자·토큰 형태의 경로 조각은 `{redacted}`로 바꿉니다. 남은 경로도 개인정보나 식별자를 포함할 수 있으므로 사람이 검토해야 합니다. PATCH 템플릿은 안전한 테스트 본문을 사람이 정할 때까지 실행할 수 없습니다.

## OpenAPI JSON에서 시나리오 초안 만들기

OpenAPI 3.0/3.1 JSON 명세를 읽어 지정한 서버 경로와 접두사 아래의 GET/PATCH 템플릿을 추출합니다. YAML, 원격 명세 다운로드, `$ref` 해석은 지원하지 않습니다. 서버 주소는 명세에서 자동 선택하지 않고 사용자가 `--origin`과 필요하면 `--server-url`로 지정합니다.

```powershell
node .\poc\import-openapi.mjs --file=.\poc\sample-openapi.json --origin=https://app.example.test --server-url=https://app.example.test/api/v2 --prefix=/api/v2/ --out=-
```

명세의 설명, 예시, 보안 정의, 헤더, 요청/응답 본문은 초안에 넣지 않습니다. GET/PATCH 이외 동작은 건너뛰며 PATCH 본문은 사람이 fixture용으로 작성해야 합니다. HAR importer와 마찬가지로 결과는 `draft-not-runnable` 상태입니다.

## 실행

PowerShell 창 1에서 안전한 샘플 API를 시작합니다.

```powershell
cd C:\HackingProgram
node .\poc\server.mjs
```

PowerShell 창 2에서 샘플 요청을 실행합니다.

```powershell
cd C:\HackingProgram
node .\poc\run-scan.mjs
```

교차 조직 접근 취약 동작이 있을 때 결과가 어떻게 표시되는지 보려면 서버를 중지한 뒤 의도적으로 취약한 예제 모드로 다시 시작합니다.

```powershell
node .\poc\server.mjs --vulnerable
```

403 상태로 응답하면서 민감한 자료 일부를 본문에 실수로 포함하는 예시를 확인하려면 `--deny-status-but-leaks` 모드로 시작합니다. 검사기는 상태 코드가 거부를 나타내더라도 금지 문자열이 본문에 있으면 검토 대상으로 표시합니다.

일반 사용자의 관리자 권한 상승 예시를 확인하려면 `--role-vulnerable` 모드로 시작합니다. 이 모드는 다른 조직 격리는 유지하면서 역할 검증만 건너뜁니다.

관리자 전용 자료에 대한 쓰기 권한 검사는 `scenarios-role-write.json`으로 실행합니다. 취약 예시는 `--role-write-vulnerable`로 시작하며, 이는 역할 검증만 PATCH 요청에서 건너뛰고 테넌트 격리는 유지합니다.

```powershell
node .\poc\server.mjs --enable-test-writes --role-write-vulnerable
node .\poc\run-scan.mjs --config=.\poc\scenarios-role-write.json --allow-local-writes
```

기본 서버를 다시 실행하면 조직 간 접근을 차단하는 읽기 전용 예제로 돌아갑니다. 서버의 PATCH 처리는 기본적으로 꺼져 있으며, 로컬 loopback 외부를 대상으로 하는 설정은 검사기가 거부합니다.

## 제한된 쓰기 권한 검사

쓰기 시나리오는 별도 파일을 쓰며, 다음 세 조건을 모두 켜야 PATCH 요청이 전송됩니다.

1. 검사 대상이 loopback IP여야 합니다.
2. `scenarios-write.json`에 `allow_writes: true`가 있어야 합니다.
3. 검사기를 `--allow-local-writes`와 함께 실행하고, fixture도 `--enable-test-writes`로 시작해야 합니다.

fixture는 문서 제목만 메모리에서 바꿉니다. 서버를 재시작하면 원래 샘플 상태로 돌아옵니다. 취약 예시를 보려면 서버를 `--enable-test-writes --write-vulnerable`로 시작한 뒤 동일 시나리오를 실행하세요.

```powershell
node .\poc\server.mjs --enable-test-writes
node .\poc\run-scan.mjs --config=.\poc\scenarios-write.json --allow-local-writes
```

## 시나리오 수정

`scenarios.json`에서 테스트 신원별 HTTP 헤더와 GET 경로, 기대 접근 결과를 편집합니다. 인증 방식은 Bearer 토큰에 한정되지 않으므로 Cookie나 API 키 헤더도 사용할 수 있습니다. 실제 비밀값은 설정 파일에 직접 쓰지 말고 헤더 값을 `{ "env": "ENVIRONMENT_VARIABLE_NAME" }`로 지정해 환경 변수에서 불러오세요. `allow` 시나리오에는 예상 자료가 실제 응답에 포함되는지 `expected_contains`로 지정할 수 있습니다. `deny` 시나리오는 민감한 자료 문자열을 `must_not_contain`에 지정할 수 있습니다. 해당 문자열이 403 응답 본문에 있어도 접근 노출로 분류합니다.

또한 `deny` 검사는 경로가 잘못되어 404가 나온 상황을 통과로 세지 않도록, **같은 경로를 다른 계정으로 읽는 대조 검사**(`exists_check`)를 지정해야 합니다. 대조 요청이 동일한 자료를 가리키고 다른 계정으로 성공해야 차단 판정을 통과시킵니다. 보고서에는 원본 응답을 저장하지 않고 본문 크기와 조건 일치 여부만 남깁니다.

- 2xx: `allow`
- 401 / 403 / 404: `deny` 후보. 단, 해당 자료가 소유자에게 보이는 대조 검사도 성공해야 `pass` 처리
- 그 외: `inconclusive`

응답 본문은 최대 64 KiB까지만 메모리에서 확인합니다. 더 큰 응답은 별도 유출 증거가 확인되지 않는 한 `inconclusive`로 분류합니다. 결과 보고서에는 원문을 저장하지 않습니다. 보고서를 파일 대신 터미널에만 출력하려면 `--out=-`를 사용합니다.

상태 코드 비교와 본문 표식은 제한된 휴리스틱입니다. 사용자 지정 표식은 오탐을 줄일 수 있지만 앱 전체의 권한 의도를 증명하지 않습니다. 현재 fixture는 테넌트 격리와 관리자 전용 감사 로그에 대한 읽기 시나리오를 포함합니다.

## 보안·제품 한계

- 실제 고객 시스템이나 인터넷 호스트에 연결하지 않습니다.
- 기본 설정은 GET만 보냅니다. PATCH는 fixture와 검사기 양쪽의 명시적 opt-in이 있어야 하며, fixture 내 제목만 메모리에서 변경합니다.
- 샘플 토큰은 fixture 전용이며 실제 계정 자격 증명을 넣으면 안 됩니다.
- `authorized: true`는 fixture를 소유·통제하는 실험임을 표시하는 안전 확인입니다.
- 이 PoC는 침투 테스트, 취약점 전체 탐지, 보안 인증이 아닙니다.
