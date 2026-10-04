# d-AI-so 프로젝트 규칙

## 저장소 구조

- 루트: 새 구조를 만드는 곳. 언어로 나눈다
  - `frontend/`: TypeScript 전부(Electron `desktop/`, 화면 `web/`). npm 하나만 쓰고 `frontend/` 안에서만 돈다
  - `backend/`: C# 전부(`Daiso.Host` 프로세스, 탭 엔드포인트, 테스트). dotnet 은 `backend/` 안에서만 돈다
  - `docs/`: 여러 모듈에 걸친 문서. 한 모듈 것은 그 모듈 폴더의 `README.md`에 둔다
- `old/`: 2026-10-03 까지의 WinUI 앱 전부(소스·테스트·도구·문서). 0.2.0 배포판이 여기서 나왔다. 새 구조를 만들 때 참고하고, 백엔드 프로젝트(Core·Providers·Infrastructure)는 `Daiso.Host`가 그대로 참조한다. `old/src/Daiso.App`은 얼려 둔다

## 문서
- **Electron 전환 (진행 중)**: `docs/README.md` 부터 읽는다. 진행 상황·일하는 방식·문서 목록이 있다. 단계는 `docs/ROADMAP.md`, 결정은 `docs/DECISIONS.md`
- 요구사항: `old/docs/REQUIREMENTS.md`
- 아키텍처: `old/docs/ARCHITECTURE.md` (인터페이스·파싱 규칙의 정본. 백엔드 코드가 이를 따른다)
- 목표·완료 기준: `old/docs/GOAL.md`
- 도구 플러그인: `old/docs/PLUGIN_PLAN.md` (**Stage 0~6 전부 완료.** 어셈블리(in-proc) 방식은 §11 에서 안 하기로 결정)
- 배포 규칙: `old/docs/RELEASE.md` (설치 프로그램이 무엇을 담고 무엇을 담지 않는지. 의존성을 더하거나 뺄 때 먼저 고친다)
- 그 밖의 지난 계획 문서: `old/docs/`

## 빌드·실행 확인

**옛 앱 (`old/`)**
옛 앱을 띄워 확인할 때는 `old/tools/run-app.ps1`을 쓴다. 빌드한 바로 그 산출물을 실행한다.
`dotnet build`(솔루션)와 프로젝트 단독 빌드의 산출물 폴더는 같아야 하며(csproj의 기본 Platform x64), 손으로 적은 경로로 실행하지 않는다.
문구 키를 지우거나 바꾸면 `old/tests/Daiso.Core.Tests`의 `StringResourceKeysTests`가 잡는다. 테스트가 빨간데 눈으로 넘기지 않는다.
`old/src`의 백엔드를 고쳤으면 `old/`에서 `dotnet build Daiso.sln`·`dotnet test Daiso.sln`도 초록이어야 한다.

**새 구조**
- 프런트: `frontend/`에서 `npm run lint`·`npm run format:check`
- 백엔드: `backend/`에서 `dotnet build`·`dotnet test` (Stage 0부터)

## 커밋

커밋은 작업 단위 별로 필수로 커밋합니다.
**커밋한 뒤에는 바로 `git push origin main` 합니다.** 로컬에만 쌓아두지 않습니다.
특별한 상황이 아니면 main 브랜치에서 작업합니다.

이 저장소는 private입니다. 소스는 여기에만 둡니다.
공개 배포가 필요하면 별도의 public 저장소에 빌드 산출물(zip)만 올립니다.

**형식**

`타입: 내용` 또는 `타입(영역): 내용`

**타입**

| 타입 | 사용할 때 |
|---|---|
| `feat` | 새 기능, 새 메카닉 추가 |
| `fix` | 동작하지 않던 것 수정 |
| `refactor` | 동작은 같고 코드 구조만 변경 |
| `chore` | 설정, YAML 값 조정, 잡일 |
| `docs` | 주석, 재현 절차서, 문서 |

**작성 규칙**

- 한글로 씁니다.
- 한 줄로 씁니다. 마침표는 붙이지 않습니다.
- "수정", "변경"만 쓰지 말고 무엇을 어떻게 했는지 적습니다.
  - 나쁨: `fix(zone3): 버그 수정`
  - 좋음: `fix(zone3): 페이즈 전환이 매 타격마다 발동하던 문제 수정`

**커밋 전 확인**

- [ ] 다른 구역 파일을 건드리지 않았는가
- [ ] 커밋 메시지에 소스 로직이나 명령어가 노출되지 않았는가

**커밋 후**

- [ ] `git push origin main` 했는가

<!-- daiso:start -->
@PROJECT_RULES.daiso
Rules above are YAML. Priority MUST > SHOULD > MAY.
<!-- daiso:end -->
