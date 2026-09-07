# GOAL — d-AI-so v1.0

## 완성 상태 (이것이 목표)
Windows에서 실행되는 WinUI 3 앱 **d-AI-so**가 다음을 모두 할 수 있다.

1. Claude Code / Codex CLI의 설치·로그인 상태를 토큰 노출 없이 보여준다
2. 선택한 폴더에서 Claude 또는 Codex 터미널을 연다 (세션 이어서 열기 포함)
3. 두 도구의 모든 로컬 세션을 프로젝트별로 나열·검색·정리(휴지통)·Markdown 내보내기한다
4. 폴더가 AI에게 전달하는 컨텍스트(CLAUDE.md, AGENTS.md, 규칙 등)를 한눈에 보여주고 중복·충돌을 리포트한다
5. `PROJECT_RULES.daiso`를 트리 UI로 만들고·열고·편집·다른 이름으로 저장하며, 마크다운 미리보기를 실시간 표시하고, CLAUDE.md/AGENTS.md에 연동 블록을 넣는다
6. 세션 로그 기반 토큰 사용량을 일별·프로젝트별·모델별로 보여준다

**최종 수락 기준**: `docs/REQUIREMENTS.md` 1~7절의 모든 항목이 앱에서 동작하고, `dotnet test` 전체 통과, 앱을 이 머신에서 실행해 위 6개를 수동 확인한 기록이 README에 있다.

참조 문서 (반드시 먼저 읽을 것):
- `docs/REQUIREMENTS.md` — 무엇을 만드는가
- `docs/ARCHITECTURE.md` — 계층, 모델, 인터페이스 시그니처, Provider 파싱 규칙. **이름·시그니처·규칙을 그대로 쓴다**
- `samples/PROJECT_RULES.daiso` — .daiso 정본 샘플
- `CLAUDE.md` — 커밋 규칙

---

## 실행 단계

두 단계로 나눈다. 이유: WinUI 툴체인 문제와 Core 로직 문제를 분리해 디버깅하기 위함. **Stage 1이 완료 기준을 모두 충족하기 전에 Stage 2를 시작하지 않는다.**

---

## Stage 1 — Core · Providers · Infrastructure (UI 없음)

결과물: `dotnet build` / `dotnet test` 통과 솔루션 + 검증용 콘솔 `Daiso.Cli`.

### 범위
1. `Daiso.sln`, 프로젝트 8개: Core, Providers.Claude, Providers.Codex, Infrastructure, Cli, Core.Tests, Providers.Tests, Infrastructure.Tests
2. `Daiso.Core`: 모델(ARCH 2절), 순수 인터페이스(3.1) 구현 — `RulePresetSerializer`, `RuleValidator`, `MarkdownRuleRenderer`, `InstructionMarkerWriter`, `InstructionTemplate`, `ContextAnalyzer`
3. `Daiso.Providers.Claude` / `.Codex`: `IProvider` 구현(ARCH 4절), 공용 `ProjectPathNormalizer`
4. `Daiso.Infrastructure`: `RuleFileService`, `SqliteSessionIndex`, `RecycleBinFileDisposer`, `WindowsTerminalLauncher`, `ContextInspector`, `MarkdownSessionExporter`
5. `tools/Daiso.Cli` 서브커맨드:
   `auth` · `sessions [--tool claude|codex] [--include-archived]` · `search <query>` · `usage --days N` · `rules render <path>` · `rules roundtrip <path>` · `rules install <projectDir>` · `doctor <dir> [--tool claude|codex]` · `export <sessionId> <out.md>`
6. 테스트 3개 프로젝트 (ARCH 8절 표 전부)

제외: WinUI, 세션 삭제 CLI 명령(Disposer 구현은 포함), CLAUDE.md/AGENTS.md 마이그레이션.

### Step 1. 솔루션 골격
- 프로젝트 8개 생성, 참조 연결(ARCH 1절 의존 방향), 패키지 추가
- `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`
- Core 어셈블리가 `System.IO.File`/`Directory`/`Process`를 참조하지 않음을 리플렉션으로 확인하는 테스트 1개
- `.gitignore` (bin/obj, .omc/, *.user)
- 완료: `dotnet build` 경고 0, 오류 0

### Step 2. Rule 모델 + YAML + 검증
- `RulePresetSerializer.Parse/Serialize`, `RuleValidator`, `RuleParseException(line, column, message)`
- 완료 기준:
  - `S = Serialize(Parse(sample))` 일 때 `Serialize(Parse(S)) == S`
  - ARCH 2.1 검증 규칙 표의 오류 행마다 테스트 1개 이상, 예외에 line/column 포함
  - `priority` 생략 → Should, 출력에는 항상 기록
  - 출력 키 순서 고정, `description` null 생략

### Step 3. 마크다운 렌더 + 마커 + 템플릿
- `MarkdownRuleRenderer`, `InstructionMarkerWriter.Apply`, `InstructionTemplate.For`
- 완료 기준:
  - 샘플 렌더 스냅샷 + 괄호 규칙 5케이스(ARCH 8절)
  - `Apply`: null / 마커 없음 / 마커 있음 × CRLF / LF. 마커 밖 바이트 동일
  - `For(Claude)`에 `@PROJECT_RULES.daiso`, `For(Codex)`에 파일명과 "MUST > SHOULD > MAY" 포함

### Step 4. Provider 파서 + 인증
- fixture (`tests/fixtures/claude/`, `tests/fixtures/codex/`), ARCH 8절 fixture 규칙. Codex는 구형·신형 2종 필수
- 완료 기준:
  - Claude: UserMessageCount가 tool_result·isMeta·sidechain 제외 값. FirstPrompt가 사용자 텍스트. synthetic 모델 usage 제외
  - Codex 구형/신형 모두 UserMessageCount ≥ 1. `world_state` 텍스트가 메시지 출력에 없음. token_count 마지막 non-null
  - `ReadMessagesAsync(path, fromByteOffset)` 중간 오프셋 이어 읽기 정확
  - Auth: Claude `refreshTokenExpiresAt`, Codex access_token JWT `exp` 기준. 상태 4종. 직렬화 결과에 `DUMMY_TOKEN` 미포함
  - `ProjectPathNormalizer`: `c:\a\b\` 와 `C:\a\b` 동일
  - Slow: 20MB fixture 스트리밍, 피크 메모리 < 파일 크기 × 2

### Step 5. SQLite 인덱스
- `sessions(…, last_offset)`, `messages_fts`(FTS5 trigram), `usage_daily`
- 완료 기준:
  - Rebuild → List 건수 = fixture 수. `IncludeArchived=false` 동작
  - Search: 3글자 부분 문자열 히트, 2글자는 LIKE 폴백 히트
  - Refresh 3케이스: 변화 없음 → 미호출 / append → 이전 offset / size 감소 → offset 0
  - Usage: Claude 일별 합산, Codex 세션 단위 덮어쓰기

### Step 6. Infrastructure 나머지 + CLI
- Launcher는 명령 문자열 생성과 프로세스 실행을 분리, 생성 함수만 테스트
- 완료 기준:
  - Launcher 문자열: wt 유/무 × pwsh/powershell/cmd. 항상 셸로 감싸짐. `codex`는 `.cmd` 셸명만
  - Disposer: IsActive 세션 Skipped, 임시 파일 1건 휴지통 이동
  - CLI 실제 실행(이 머신): `auth` 토큰 없음 / `sessions` 목록 / `rules roundtrip` → OK / `rules install <임시폴더>` 두 번 실행 동일 / `doctor . --tool claude` / `export` 파일 생성

### Step 7. Stage 1 마무리
- README: 빌드/테스트/CLI 사용법
- `dotnet test` 통과 (`--filter Category!=Slow` 기본, Slow 1회 별도 실행 보고)
- **Stage 1 게이트**: 위 Step 1~6 완료 기준 전부 충족 확인 후 Stage 2 진입

---

## Stage 2 — WinUI 3 앱

결과물: `Daiso.App` 실행 파일. Stage 1의 인터페이스만 사용하며 App 프로젝트에는 파싱·파일 로직을 두지 않는다.

### 범위
- `src/Daiso.App` (net8.0-windows10.0.19041, Windows App SDK, unpackaged)
- CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection
- 페이지 5개 + Shell. 설정 저장 `%LOCALAPPDATA%\d-AI-so\settings.json`

### Step 8. App 골격
- WinUI 3 프로젝트 생성, `Daiso.sln`에 추가, Core/Providers/Infrastructure 참조
- `App.xaml.cs` DI 컨테이너: ARCH 3절 인터페이스 → 구현, `IEnumerable<IProvider>`
- Shell: `NavigationView` 5 항목(Dashboard, Terminal, Sessions, RuleMaker, Settings), Mica 배경, 라이트/다크 자동
- 완료: 빌드 후 실행되어 5개 페이지 간 이동. 첫 실행에 `ISessionIndex.RefreshAsync`를 백그라운드로 시작하고 진행률을 상태바에 표시

### Step 9. Dashboard 페이지
- 도구별 카드 2개: 설치 여부, `AuthStatus` 배지(🟢/🟡/🔴), 계정 라벨·이메일, 만료 시각, Extras 목록, "로그인 하기" 버튼(터미널로 `claude` / `codex login`)
- 세션 총 수·총 용량, 최근 7일 토큰 사용량 요약
- 완료: 이 머신에서 실제 상태 표시. 화면 어디에도 토큰 문자열 없음(UI 자동화 또는 ViewModel 직렬화 테스트로 확인)

### Step 10. Terminal 페이지
- 폴더 선택(FolderPicker) + 최근 폴더 10개 히스토리
- Claude / Codex 버튼, 옵션 인자 프리셋(REQ 4절), 자유 입력 인자
- 완료: wt 없는 이 머신에서 셸 창이 열리고 해당 도구가 실행됨

### Step 11. Sessions 페이지
- 좌: 프로젝트 트리(정규화 경로, 세션 수, 용량, 고아 표시). 우: 세션 목록(REQ 5.1 열 전부)
- 필터 바: 도구 / 기간 / 고아만 / 크기 / 아카이브 포함
- 검색 상자: `SearchAsync` 결과를 프로젝트 > 세션 > 매칭 문장 트리로 표시, 항목 클릭 → 세션 선택
- 세션 상세: 메시지 타임라인(User/Assistant, Tool은 접힘), "이어서 열기"(Terminal로 resume 인자 전달), "Markdown 내보내기"(FileSavePicker)
- 정리: 다중 선택 삭제, 규칙 기반 일괄 선택(N일 이상 / 고아 / N MB 초과), 삭제 전 미리보기 대화상자(건수·회수 용량·실행 중 제외 목록), 기본 휴지통, 영구 삭제는 2차 확인
- 완료: 실제 세션으로 검색·상세·내보내기 동작. 삭제는 임시로 만든 더미 jsonl 폴더를 세션 루트로 지정해 검증(실제 세션 삭제 금지)

### Step 12. RuleMaker 페이지
- 파일 메뉴: 새로 만들기 / 열기 / 저장 / 다른 이름으로 저장 / 최근 파일 / 프리셋 라이브러리(`%LOCALAPPDATA%\d-AI-so\presets`)에 저장·불러오기
- 헤더 편집: name, description
- Global 행동 목록: 추가/삭제/순서 이동, 각 행 텍스트 + Priority 콤보
- Rule 목록: 각 Rule은 조건 트리 편집기 + 행동 목록
  - 조건 트리: 노드 추가(리프 / AND / OR / NOT), 삭제, 드래그 이동, 그룹으로 감싸기
  - 트리 아래 한 줄 미리보기 `A & (B | C)`
- 오른쪽 패널: `IMarkdownRuleRenderer` 실시간 미리보기
- 검증 오류는 저장 시 대화상자에 line/column 표시
- "프로젝트에 연동" 버튼: 폴더 선택 → `.daiso` 저장 + `EnsureInstruction`
- 완료: `samples/PROJECT_RULES.daiso` 열기 → 편집 → 다른 이름으로 저장 → 다시 열기 시 동일 렌더. 연동 버튼으로 임시 폴더에 CLAUDE.md/AGENTS.md 생성 확인

### Step 13. Context Doctor (Sessions 페이지 프로젝트 상세 또는 별도 탭)
- 프로젝트 선택 시 도구 토글(Claude/Codex) → `IContextInspector` 결과: 파일 목록(로드 순서, 존재 여부, 글자 수), 총 글자 수, 중복 줄 목록, 충돌 후보 목록. 파일 클릭 시 내용 미리보기
- REQ 5.3의 부가 정보(settings 요약, skills/agents 수, git 브랜치)
- 완료: 이 저장소 폴더로 리포트 표시

### Step 14. Usage 뷰 (Dashboard 하위 또는 별도 탭)
- 기간 선택(7일/30일/전체), 일별 막대 그래프, 프로젝트별 상위 10, 모델별 비율 표
- 단가표 편집(Settings), 비용은 "추정" 라벨
- 완료: 실제 인덱스 데이터로 표시. 단가 변경 시 즉시 재계산

### Step 15. Settings 페이지
- 세션 루트 경로 재정의(기본값 표시), 정리 규칙 기본값, 단가표, 인덱스 재구축 버튼, 테마
- 완료: 변경 후 재시작해도 유지

### Step 16. 최종 마무리
- 앱 아이콘, 창 크기 기억, 예외 전역 핸들러(사용자에게 대화상자, 로그 파일에 스택 — 토큰 마스킹 확인)
- README: 실행 방법, 스크린샷, 수동 확인 체크리스트(완성 상태 6항목) 결과 기록
- `dotnet test` 전체 통과
- **최종 게이트**: 완성 상태 6항목 수동 확인 완료

---

## 하지 말 것 (전 단계 공통)
- `.credentials.json`, `.claude.json`, `auth.json`, 실제 세션 jsonl 에 쓰기·삭제 금지. 삭제 기능 검증은 더미 폴더로만
- Core에서 파일·경로·프로세스 접근 금지. App에서 파싱·파일 로직 작성 금지(Infrastructure 인터페이스 호출만)
- ARCHITECTURE 시그니처·파싱 규칙 임의 변경 금지. 필요하면 `docs/ARCHITECTURE.md` 먼저 수정 후 커밋 메시지에 이유
- fixture에 실제 경로·대화·토큰 포함 금지
- 네트워크 호출 금지 (NuGet 복원 제외)
- 시스템 기본 인코딩 사용 금지. 모든 텍스트 IO는 UTF-8 명시
- Stage 1 게이트 미통과 상태에서 Stage 2 착수 금지
- 완료 기준 미충족 Step을 완료로 보고하지 않음. 실패 테스트는 출력 그대로 보고
- 커밋은 Step 단위, 메시지는 `CLAUDE.md` 규칙

## 산출물 체크리스트
- [ ] Stage 1: 8개 프로젝트 빌드(경고 0), 테스트 3종 통과, Slow 1회 통과, fixture(Claude 1+, Codex 구형·신형), CLI 9개 실행 확인
- [ ] Stage 2: Daiso.App 실행, 페이지 5개, 완성 상태 6항목 수동 확인
- [ ] README (빌드·테스트·실행·확인 기록)
- [ ] Step별 커밋 16개
