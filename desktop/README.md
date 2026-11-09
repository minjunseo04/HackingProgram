# BoundaryCheck Windows desktop app

현재 버전: **0.1.1**

## 실행

`website/downloads/BoundaryCheck-win-x64.zip` 압축을 풀고 `BoundaryCheck.exe`를 실행합니다. Windows x64용 자체 포함 파일이라 별도 .NET 설치가 필요하지 않습니다.

## v0.1.1 기능

- 프로젝트 이름, 대상 origin, API 경로 저장
- HAR 또는 OpenAPI 3.0/3.1 JSON에서 GET/PATCH 요청 경로 가져오기
- 긴 숫자, UUID, 토큰처럼 보이는 경로 조각 가리기 및 중복 요청 합치기
- 요청별 기대 접근값(미지정/허용/거부) 편집
- 작업 내용을 `%LOCALAPPDATA%\BoundaryCheck\project.json`에 저장
- 첫 화면에서 로컬 저장과 원격 전송 여부를 안내
- 화면 배율에 맞춰 창과 입력 영역 크기 조정

이 버전은 HTTP 요청을 실제로 보내지 않습니다. API 신원 설정, 검사 실행, 결과 판정과 보고서는 아직 구현되지 않았습니다. 현재 지원 언어는 한국어이며, 다른 언어가 실제로 번역되면 언어 설정을 추가합니다. 사용 정보 전송 기능도 없으므로 정보 활용 동의는 받지 않습니다. 추후 원격 분석이나 계정 기능을 넣게 되면 수집 항목·목적·보관 기간을 알리고 선택 동의를 먼저 추가합니다. 가져온 요청과 기대값을 사람이 검토해야 하며 실제 비밀이 든 HAR 파일을 공유하거나 외부로 업로드하지 마세요.

## 소스에서 빌드

`.NET 10 Windows Desktop SDK`가 필요합니다.

```powershell
dotnet build .\desktop\BoundaryCheck.csproj --configuration Release
```
