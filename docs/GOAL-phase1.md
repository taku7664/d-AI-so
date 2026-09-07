# GOAL — Phase 1: Core + Providers + Infrastructure (UI 없음)

## 목표
d-AI-so의 UI 아래 계층을 완성한다. WinUI는 이 Phase에서 만들지 않는다.
결과물은 `dotnet build` / `dotnet test`가 통과하는 솔루션과, 실제 로컬 환경에서 동작을 확인할 수 있는 콘솔 도구다.

참조 문서 (반드시 먼저 읽을 것):
- `docs/REQUIREMENTS.md` — 무엇을 만드는가
- `docs/ARCHITECTURE.md` — 계층, 모델, 인터페이스 시그니처, Provider 파싱 규칙. **여기 정의된 이름·시그니처·규칙을 그대로 쓴다**
- `samples/PROJECT_RULES.daiso` — .daiso 정본 샘플

## 범위
포함:
1. 솔루션 `Daiso.sln`, 프로젝트 8개: Core, Providers.Claude, Providers.Codex, Infrastructure, Cli, Core.Tests, Providers.Tests, Infrastructure.Tests
2. `Daiso.Core`: 모델(ARCH 2절), 순수 인터페이스(3.1)와 구현 — `RulePresetSerializer`, `RuleValidator`, `MarkdownRuleRenderer`, `InstructionMarkerWriter`, `InstructionTemplate`, `ContextAnalyzer`
3. `Daiso.Providers.Claude`, `Daiso.Providers.Codex`: `IProvider` 구현 (ARCH 4절), 공용 `ProjectPathNormalizer`
4. `Daiso.Infrastructure`: `RuleFileService`, `SqliteSessionIndex`, `RecycleBinFileDisposer`, `WindowsTerminalLauncher`, `ContextInspector`, `MarkdownSessionExporter`
5. `tools/Daiso.Cli`: 검증용 콘솔. 서브커맨드
   `auth` · `sessions [--tool claude|codex] [--include-archived]` · `search <query>` · `usage --days N` · `rules render <path>` · `rules roundtrip <path>` · `rules install <projectDir>` · `doctor <dir> [--tool claude|codex]` · `export <sessionId> <out.md>`
6. 테스트 프로젝트 3개 (ARCH 8절 표 전부)

제외: WinUI, 설정 UI, 세션 삭제 UI(Disposer 구현은 포함, CLI 명령은 없음), CLAUDE.md/AGENTS.md 마이그레이션.

## 단계와 완료 기준

### Step 1. 솔루션 골격
- `dotnet new sln`, 프로젝트 8개 생성, 참조 연결(ARCH 1절 의존 방향), 패키지 추가
- `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`
- Core 프로젝트에 `System.IO.File`/`Directory`/`Process` 사용 금지를 확인하는 테스트 1개 (리플렉션으로 Core 어셈블리의 참조 타입 검사)
- 완료: `dotnet build` 경고 0, 오류 0

### Step 2. Rule 모델 + YAML + 검증
- `RulePresetSerializer.Parse/Serialize`, `RuleValidator`, `RuleParseException(line, column, message)`
- 완료 기준:
  - `S = Serialize(Parse(sample))` 일 때 `Serialize(Parse(S)) == S`
  - ARCH 2.1 검증 규칙 표의 오류 행마다 테스트 1개 이상. 각 예외에 line/column 포함
  - `priority` 생략 → Should, 출력에는 항상 기록
  - 출력 키 순서 고정, `description` null 생략

### Step 3. 마크다운 렌더 + 마커 + 템플릿
- `MarkdownRuleRenderer`, `InstructionMarkerWriter.Apply`, `InstructionTemplate.For`
- 완료 기준:
  - 샘플 렌더 스냅샷 + 괄호 규칙 5케이스(ARCH 8절)
  - `Apply`: null / 마커 없음 / 마커 있음 × CRLF / LF. 마커 밖 바이트 동일
  - `For(Claude)`에 `@PROJECT_RULES.daiso` 포함, `For(Codex)`에 파일명과 "MUST > SHOULD > MAY" 포함

### Step 4. Provider 파서 + 인증
- fixture 작성 (`tests/fixtures/claude/`, `tests/fixtures/codex/`). ARCH 8절 fixture 규칙 준수. Codex는 구형·신형 2종 필수
- 완료 기준:
  - Claude: UserMessageCount가 tool_result·isMeta·sidechain을 제외한 값. FirstPrompt가 사용자 텍스트. synthetic 모델 usage 제외
  - Codex 구형/신형 fixture 모두 UserMessageCount ≥ 1. `world_state` 텍스트가 `ReadMessagesAsync` 출력에 없음. token_count 마지막 non-null 값
  - `ReadMessagesAsync(path, fromByteOffset)`: 중간 오프셋에서 시작해 이어 읽기 정확
  - Auth: Claude는 `refreshTokenExpiresAt` 기준, Codex는 access_token JWT `exp` 기준. 상태 Missing/Expired/ExpiringSoon/LoggedIn 4종. 직렬화 결과에 `DUMMY_TOKEN` 미포함
  - `ProjectPathNormalizer`: `c:\a\b\` 와 `C:\a\b` 동일
  - Slow: 20MB fixture 스트리밍, 피크 메모리 < 파일 크기 × 2

### Step 5. SQLite 인덱스
- 스키마: `sessions(…, last_offset)`, `messages_fts` (FTS5 trigram), `usage_daily`
- 완료 기준:
  - Rebuild → List 건수 = fixture 수. `IncludeArchived=false` 필터 동작
  - Search: 3글자 부분 문자열(예 "더미질") 히트, 2글자("더미")는 LIKE 폴백으로 히트
  - Refresh: 가짜 IProvider로 (a) 변화 없음 → ReadMessagesAsync 미호출 (b) append → 이전 last_offset으로 호출 (c) size 감소 → offset 0으로 호출
  - Usage: Claude 일별 합산, Codex 세션 단위 덮어쓰기 검증

### Step 6. Infrastructure 나머지 + CLI
- `RuleFileService`, `RecycleBinFileDisposer`, `WindowsTerminalLauncher`, `ContextInspector`, `MarkdownSessionExporter`
- Launcher는 "명령 문자열 생성"과 "프로세스 실행"을 분리. 생성 함수만 테스트
- 완료 기준:
  - Launcher 문자열: wt 있음/없음 × pwsh/powershell/cmd. 명령은 항상 셸로 감싸짐. `codex`는 `.cmd` 셸명만
  - Disposer: IsActive 세션은 Skipped, 임시 파일 1건 휴지통 이동 확인
  - CLI 실제 실행 (이 머신):
    - `auth` → Claude/Codex 상태·계정 라벨 출력, 토큰 문자열 없음
    - `sessions` → 오류 없이 목록. `--include-archived`로 Codex 아카이브 포함
    - `rules roundtrip samples/PROJECT_RULES.daiso` → `OK`
    - `rules install <임시폴더>` → CLAUDE.md에 `@PROJECT_RULES.daiso`, AGENTS.md에 지시문 생성. 두 번 실행해도 결과 동일
    - `doctor . --tool claude` → 파일 목록·글자 수 출력
    - `export <아무 세션> out.md` → 파일 생성

### Step 7. 마무리
- `README.md`: 빌드/테스트/CLI 사용법
- `dotnet test` 전체 통과 (`--filter Category!=Slow` 기본, Slow 별도 1회 실행해 결과 보고)
- Step 단위 커밋. 메시지는 프로젝트 `CLAUDE.md` 커밋 규칙을 따른다 (예: `feat(core): .daiso YAML 직렬화와 검증 규칙 구현`)

## 하지 말 것
- WinUI 프로젝트 생성 금지 (Phase 2)
- `.credentials.json`, `.claude.json`, `auth.json`, 세션 jsonl 에 쓰기·삭제 금지. CLI에 삭제 명령 넣지 않음
- Core에서 파일·경로·프로세스 접근 금지. 위반 시 Step 1 테스트가 잡아야 함
- ARCHITECTURE 시그니처·파싱 규칙 임의 변경 금지. 필요하면 `docs/ARCHITECTURE.md` 먼저 수정 후 커밋 메시지에 이유
- fixture에 실제 경로·대화·토큰 포함 금지
- 네트워크 호출 금지 (NuGet 복원 제외)
- 시스템 기본 인코딩 사용 금지. 모든 텍스트 IO는 UTF-8 명시
- 완료 기준 미충족 Step을 완료로 보고하지 않음. 실패 테스트는 출력 그대로 보고

## 산출물 체크리스트
- [ ] Daiso.sln + 8개 프로젝트 빌드 (경고 0)
- [ ] Core.Tests / Providers.Tests / Infrastructure.Tests 통과, Slow 1회 통과
- [ ] tests/fixtures (Claude 1종 이상, Codex 구형·신형)
- [ ] tools/Daiso.Cli 9개 서브커맨드 실제 실행 확인
- [ ] README 갱신
- [ ] Step별 커밋 7개
