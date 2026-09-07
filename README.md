# d-AI-so

AI에 필요한게 다이소.

Claude Code와 Codex CLI를 Windows에서 함께 다루는 도구다. 로그인 상태 확인, 폴더에서 터미널 열기,
로컬 세션 나열·검색·정리·내보내기, 폴더가 AI에게 넘기는 컨텍스트 점검, `PROJECT_RULES.daiso` 규칙 편집,
세션 로그 기반 토큰 사용량 통계를 한 곳에서 한다.

## 지금 상태

**Stage 1 (Core · Providers · Infrastructure + 검증용 CLI) 완료.** Stage 2 (WinUI 3 앱)는 아직이다.

- 무엇을 만드는가: [docs/REQUIREMENTS.md](docs/REQUIREMENTS.md)
- 어떻게 나누고 연결하는가: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- 목표와 완료 기준: [docs/GOAL.md](docs/GOAL.md)

## 구조

| 프로젝트 | TFM | 역할 |
|---|---|---|
| `src/Daiso.Core` | net8.0 | 모델·인터페이스·순수 로직. 파일·경로·프로세스를 다루지 않는다 |
| `src/Daiso.Providers.Common` | net8.0 | 두 Provider 공용 (경로 정규화, jsonl 스트리밍, JWT exp 판독) |
| `src/Daiso.Providers.Claude` | net8.0 | Claude Code 세션·인증 파서 |
| `src/Daiso.Providers.Codex` | net8.0 | Codex 세션·인증 파서 (구형·신형 형식 모두) |
| `src/Daiso.Infrastructure` | net8.0-windows | SQLite 인덱스, 파일 IO, 터미널 실행, 휴지통 |
| `tools/Daiso.Cli` | net8.0-windows | Stage 1 검증용 콘솔 (`daiso`) |
| `tests/Daiso.Core.Tests` | net8.0 | Core 단위 테스트 |
| `tests/Daiso.Providers.Tests` | net8.0 | Provider 파서·인증 테스트 (fixture 기반) |
| `tests/Daiso.Infrastructure.Tests` | net8.0-windows | 인덱스·런처·삭제·규칙 파일 테스트 |

Core의 순수성(파일·경로·프로세스 미사용)은 어셈블리 IL 메타데이터를 검사하는 테스트로 강제한다.

## 빌드

.NET SDK 8 이상이 필요하다. 이 저장소는 SDK 10에서 `net8.0`을 타겟해 빌드한다 (`global.json`).

```bash
dotnet build Daiso.sln
```

경고 0, 오류 0이 정상이다 (`TreatWarningsAsErrors=true`).

## 테스트

```bash
dotnet test Daiso.sln --filter "Category!=Slow"
```

20MB 세션 스트리밍처럼 오래 걸리는 테스트는 `Category=Slow`로 빠져 있다. 따로 돌린다.

```bash
dotnet test Daiso.sln --filter "Category=Slow"
```

### 마지막 확인 결과 (2026-09-07, Windows 10 Home, .NET SDK 10.0.400)

| 대상 | 결과 |
|---|---|
| `dotnet build Daiso.sln` | 경고 0, 오류 0 |
| `Daiso.Core.Tests` | 87건 통과 |
| `Daiso.Providers.Tests` | 70건 통과 |
| `Daiso.Infrastructure.Tests` | 42건 통과 |
| `Category=Slow` (20MB 스트리밍) | 2건 통과 |

## CLI 사용법

빌드한 실행 파일은 `tools/Daiso.Cli/bin/Debug/net8.0-windows/daiso.exe`다.

```bash
dotnet run --project tools/Daiso.Cli -- auth
```

| 명령 | 하는 일 |
|---|---|
| `daiso auth` | 두 도구의 설치·로그인 상태. 토큰 값은 출력하지 않는다 |
| `daiso sessions [--tool claude\|codex] [--include-archived]` | 인덱스의 세션 목록 |
| `daiso search <query>` | 세션 본문 검색 (3글자 이상은 trigram, 2글자는 LIKE 폴백) |
| `daiso usage --days N` | 최근 N일 토큰 사용량 (일별·프로젝트별·모델별) |
| `daiso rules render <path>` | `.daiso`를 마크다운 미리보기로 |
| `daiso rules roundtrip <path>` | `.daiso` 직렬화 안정성 검사 |
| `daiso rules install <projectDir>` | 폴더의 `CLAUDE.md`/`AGENTS.md`에 daiso 마커 블록을 넣거나 갱신 |
| `daiso doctor <dir> [--tool claude\|codex]` | 폴더의 컨텍스트 파일 목록·글자 수·중복 줄·충돌 후보 |
| `daiso export <sessionId> <out.md>` | 세션을 마크다운으로 내보내기 |

`sessions` / `search` / `usage` / `export`는 인덱스가 없으면 먼저 만든다.
인덱스는 기본으로 `%LOCALAPPDATA%\d-AI-so\index.db`에 만들어진다.
다른 드라이브에 두려면 `DAISO_INDEX_DB` 환경 변수에 파일 경로를 준다.

```bash
DAISO_INDEX_DB=F:\daiso\index.db dotnet run --project tools/Daiso.Cli -- sessions
```

> 인덱스는 세션 본문의 사용자·어시스턴트 텍스트를 담으므로 세션이 많으면 수백 MB가 될 수 있다.
> 실제 확인에서 세션 242건(원본 약 1.3GB)에 대해 인덱스가 약 205MB였다.

### Stage 1 실제 실행 확인 (2026-09-07, 이 머신)

| 명령 | 결과 |
|---|---|
| `auth` | Claude·Codex 모두 `LoggedIn`, 계정·만료 시각 표시, 출력에 토큰 문자열 없음 |
| `sessions` | 242건 나열 (Claude·Codex, 보관 포함) |
| `search "GOAL.md"` | 8건, 프로젝트 > 메시지 트리로 표시 |
| `usage --days 7` | 일별·프로젝트별 상위 10·모델별 표시 |
| `rules roundtrip samples/PROJECT_RULES.daiso` | OK |
| `rules render samples/PROJECT_RULES.daiso` | REQ 6.4 양식으로 출력 |
| `rules install <임시폴더>` | 두 번 실행해도 두 파일 해시 동일 |
| `doctor . --tool claude` | 컨텍스트 파일 15개(존재 3개), 총 1,204자 |
| `export <codex 세션>` | 144,211 바이트 마크다운 생성 |

## 안전 규칙

- CLI가 만든 파일(`.credentials.json`, `.claude.json`, `auth.json`, 세션 jsonl)은 읽기 전용으로 다룬다
- 인증 토큰·API 키 값은 화면·로그·파일·예외 메시지에 쓰지 않는다
- `CLAUDE.md` / `AGENTS.md` 수정은 `<!-- daiso:start -->` ~ `<!-- daiso:end -->` 블록 안으로만 한다.
  블록 밖은 개행 문자까지 그대로 둔다
- 세션 삭제는 기본 휴지통, 실행 중 세션은 거부한다
- 네트워크 호출은 없다 (NuGet 복원 제외)
- 모든 텍스트 IO는 UTF-8 명시
