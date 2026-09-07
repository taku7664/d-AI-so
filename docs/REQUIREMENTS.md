# d-AI-so 요구사항 정리 (v0.3)

> 로컬 전용 AI CLI 유틸리티. Claude Code / Codex CLI를 대상으로 인증 상태 확인, 터미널 실행, 세션 관리, 규칙(Rule) 프리셋 편집을 제공한다.
> 작성일: 2026-09-07

---

## 0. 범위와 전제

| 항목 | 결정 |
|---|---|
| 플랫폼 | Windows 10/11, WinUI 3 (Windows App SDK), C# / .NET 8 |
| 지원 도구 | Claude Code, Codex CLI (확장 가능한 Provider 구조) |
| 네트워크 | 없음. 로컬 파일 읽기/쓰기 + 프로세스 실행만 |
| 계정/로그인 | 앱 자체 로그인 없음. CLI가 남긴 인증 파일을 읽기만 함 |
| 배포 | 미정 (MSIX 또는 unpackaged exe) |

---

## 1. UI/UX (WinUI 3)

- NavigationView 기반 좌측 메뉴: **Dashboard / Terminal / Sessions / Rule-Maker / Settings**
- Mica/Fluent 테마, 라이트·다크 자동 추종
- MVVM (CommunityToolkit.Mvvm), DI (Microsoft.Extensions.DependencyInjection)
- 모든 파일 시스템 접근은 `IProvider` 추상화 뒤에 둠 → Claude/Codex 외 도구(Gemini CLI 등) 추가 용이

---

## 2. Provider 지원 (Claude / Codex)

각 Provider가 노출해야 하는 정보:

| 항목 | Claude Code | Codex CLI |
|---|---|---|
| 실행 명령 | `claude` | `codex` |
| 인증 파일 | `%USERPROFILE%\.claude\.credentials.json` | `%USERPROFILE%\.codex\auth.json` |
| 세션 루트 | `%USERPROFILE%\.claude\projects\<slug>\*.jsonl` | `%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-*.jsonl` |
| 규칙 파일 | `CLAUDE.md` (프로젝트 루트 / `.claude/`) | `AGENTS.md` (프로젝트 루트) |
| 전역 설정 | `.claude\settings.json` | `.codex\config.toml` |

- Claude 세션 폴더 slug 규칙: 절대 경로에서 `:` `\` `/` 를 `-`로 치환 (예: `C:\Temp\as-95\repo` → `C--Temp-as-95-repo`)
- Codex 세션은 날짜 폴더에 평면 저장되며, 프로젝트 경로는 첫 레코드 `session_meta.cwd`에서 읽음. **로그 형식이 버전마다 다름** (0.153부터 사용자 메시지 이벤트 구조 변경) → 구형·신형 모두 파싱
- 설치 여부 확인: PATH에서 npm 셸 `claude.cmd` / `codex.cmd` 탐색 + `--version` 실행. 모든 텍스트 파일은 UTF-8로 읽음(시스템 로케일 무시)

---

## 3. 로그인(인증) 정보 확인

**읽기 전용**. 토큰 값은 절대 화면에 표시하지 않고 존재 여부·메타데이터만 표시한다.

### Claude
- `.credentials.json` → 구독 종류(`subscriptionType`), 요금 티어, 권한 범위, 연결된 MCP 커넥터 이름
- `.claude.json`의 `oauthAccount` → 이메일, 표시 이름, 조직명 (계정 라벨용)
- **만료 판정은 `refreshTokenExpiresAt` 기준**. `expiresAt`은 자동 갱신되는 단기 액세스 토큰이라 사용하지 않음 (액세스 토큰 만료 후에도 CLI 정상 동작 확인)

### Codex
- `auth.json` → `auth_mode`(chatgpt / apikey), `last_refresh`, API 키 설정 여부(값은 마스킹)
- 만료 판정은 `access_token`의 JWT `exp` 기준 (서명 검증 없이 exp 숫자만 추출). API 키 모드는 만료 없음

### 공통
- 상태 배지: 🟢 로그인됨 / 🟡 7일 내 만료 / 🔴 없음·만료
- "로그인 하기" 버튼 → 터미널에서 `claude` / `codex login` 실행 (앱이 직접 인증하지 않음)
- 백업 파일(`auth.*.bak`) 목록 표시 (선택)

---

## 4. 터미널 열기

- 대상 폴더 선택(최근 폴더 히스토리 포함) 후 **Claude** 또는 **Codex** 버튼
- 실행 대상은 npm이 설치한 `claude` / `codex` 셸(.cmd)만. Codex 데스크톱 앱이 설치한 exe는 사용하지 않음
- `.cmd` 셸은 직접 실행이 불안정하므로 **항상 셸로 감싸서 실행**: `pwsh -NoExit -Command` → `powershell` → `cmd /k` 순 폴백
- Windows Terminal(`wt.exe`)이 있으면 그 안에서 열고, 없으면 셸 창을 직접 띄움. **wt 없음이 기본 경로** (Windows 10 Home 기본 상태)
- 옵션 인자 프리셋
  - Claude: `--resume <id>`, `--continue`, `--model`, `--permission-mode`
  - Codex: `resume <id>`, `--model`, `--sandbox`
- 세션 뷰에서 "이 세션 이어서 열기" 시 resume 인자 자동 채움
- 외부 터미널만 열고 앱은 개입하지 않음 (내장 터미널은 v2 검토)

---

## 5. SessionManager

### 5.1 세션 목록
- 프로젝트(로컬 폴더) 단위로 그룹핑. 경로는 대소문자·끝 구분자 정규화 후 비교
- 열: 도구 아이콘, 세션 ID, 시작/마지막 수정 시각, 파일 크기, 사용자 메시지 수, 첫 사용자 프롬프트(요약), 폴더 존재 여부, 실행 중 표시, 아카이브 표시(Codex)
- 메시지 수와 첫 프롬프트는 **사람이 입력한 메시지만** 기준. 도구 결과·메타·서브에이전트 메시지 제외
- Codex `archived_sessions` 포함 (아카이브 배지, 필터로 제외 가능)
- 필터: 도구별 / 기간 / 폴더가 삭제된 고아 세션 / 크기 / 아카이브 포함 여부

### 5.2 정리(Cleanup)
- 개별 삭제, 다중 선택 삭제, 규칙 기반 일괄 삭제(N일 이상 / 고아 세션 / N MB 초과)
- 삭제 전 미리보기 + 총 회수 용량 표시
- **기본은 휴지통(Recycle Bin) 이동**. 영구 삭제는 별도 확인
- **실행 중인 세션은 삭제 거부** (Claude `~/.claude/sessions/<pid>.json`으로 판정)
- 내보내기: 세션을 Markdown으로 변환해 저장

### 5.3 로컬 폴더 컨텍스트 표시
선택한 프로젝트 폴더에 대해 AI가 실제로 읽는 컨텍스트를 도구별로 한눈에:
- Claude: 전역 `~/.claude/CLAUDE.md`, 상위 폴더들의 CLAUDE.md, 폴더의 `CLAUDE.md` / `.claude/CLAUDE.md` / `CLAUDE.local.md`, `.claude/rules/*.md`, `PROJECT_RULES.daiso`
- Codex: 전역 `~/.codex/AGENTS.md`, git 루트부터 폴더까지의 `AGENTS.md` 계층, `PROJECT_RULES.daiso`
- `.claude/settings.json`, `.claude/settings.local.json` (권한/훅 요약), `.mcp.json`
- `.claude/skills/`, `.claude/agents/`, `.claude/commands/` 항목 수
- 이 폴더에 속한 세션 수 / 총 용량
- Git 정보(브랜치, 마지막 커밋) — 있으면

---

## 6. Rule-Maker

### 6.1 데이터 모델
```
Preset
 ├─ name: string
 ├─ description?: string
 ├─ globalActions: Action[]          # 조건 없는 행동
 └─ rules: Rule[]
      ├─ condition: ConditionExpr    # 트리 (AND / OR / NOT / Leaf)
      └─ actions: Action[]
Action
 ├─ text: string
 └─ priority: MUST | SHOULD | MAY    # 중요도 (라벨은 설정에서 변경 가능)
```

### 6.2 조건식
- Leaf: 자연어 문장 (예: "파일이 .cs 인 경우")
- 연산자: `AND(&)`, `OR(|)`, `NOT(!)`, 그룹 `( )`
- UI: 트리 빌더 (노드 추가/그룹화/드래그) + 하단에 텍스트 미리보기 `{C1} & ({C2} | {C3})`

### 6.3 저장 형식 — `PROJECT_RULES.daiso` (YAML, 확정: 구조형)

조건식은 문자열 파싱 없이 **YAML 트리 그대로** 표현한다. 앱은 YAML 역직렬화만 하면 되고 별도 조건식 파서가 필요 없다.

```yaml
# PROJECT_RULES.daiso
daiso: 1
name: Backend Rules
description: 서버 코드 작업 시 규칙

# 조건 없이 항상 적용
global:
  - action: 한국어로 응답한다
    priority: SHOULD
  - action: 비밀키/토큰 값을 출력하지 않는다
    priority: MUST

rules:
  - when: C# 파일을 수정할 때            # 단일 조건은 문자열
    then:
      - action: 변경 전 영향 범위를 먼저 설명한다
        priority: MUST
      - action: 관련 테스트를 함께 수정한다
        priority: SHOULD

  - when:                              # 복합 조건은 and / or / not 트리
      and:
        - C# 파일을 수정할 때
        - or:
            - public API 변경
            - DB 스키마 변경
    then:
      - action: 마이그레이션 계획을 먼저 작성한다
        priority: MUST

  - when:
      테스트 코드가 아닐 때
    then:
      - action: 공개 함수에 XML 주석을 남긴다
        priority: MAY
```

스키마 요약:

| 키 | 타입 | 설명 |
|---|---|---|
| `daiso` | int | 스키마 버전 (현재 1) |
| `name` | string | 프리셋 이름 (`##` 제목) |
| `description` | string? | 설명 |
| `global` | Action[] | 조건 없는 행동 |
| `rules[].when` | Condition | 문자열(리프) 또는 `{and: [..]}` / `{or: [..]}` 중 하나. 중첩 가능 |

> 부정은 **형식에서 지원하지 않는다.** 조건이 자유 문장이라 `테스트 코드가 아닐 때`처럼 말로 쓰면 되고,
> 연산자를 하나 줄이면 트리 편집·미리보기·검증이 모두 단순해진다. `not:` 키가 있는 파일은 알 수 없는 연산자로 거부된다.
| `rules[].then` | Action[] | 행동 목록 |
| `Action.action` | string | 행동 문장 |
| `Action.priority` | MUST \| SHOULD \| MAY | 생략 시 SHOULD |

- 세션 시작 시 AI가 한 번 읽고 컨텍스트에 유지되므로 AI 측 해석 비용은 세션당 1회
- 사람용 가독성은 앱의 마크다운 미리보기(6.4)가 담당

### 6.4 미리보기 형식 — 마크다운
앱 오른쪽 패널에 YAML을 아래 양식으로 실시간 변환해 보여준다. 파일로는 저장하지 않는다(내보내기 옵션은 선택).
```
## {Preset 이름}

{조건 없는 Action} ({Priority})

### {Condition 1}

{Action 1} ({Priority})
{Action 2} ({Priority})

### {Condition 1} & ({Condition 2} | {Condition 3})

{Action}
```

### 6.5 CLAUDE.md / AGENTS.md 연동
- 프로젝트 루트의 `CLAUDE.md`, `AGENTS.md`에 **마커 블록만** 추가/갱신. 도구별 내용이 다르다.
  - Claude (import 문법 — 도구 호출 없이 매 세션 자동 인라인, 컨텍스트 압축 후에도 재주입됨):
    ```
    <!-- daiso:start -->
    @PROJECT_RULES.daiso
    Rules above are YAML. Priority MUST > SHOULD > MAY.
    <!-- daiso:end -->
    ```
  - Codex (import 없음 → 읽기 지시문):
    ```
    <!-- daiso:start -->
    Read and follow the rules in ./PROJECT_RULES.daiso (YAML). Priority MUST > SHOULD > MAY.
    <!-- daiso:end -->
    ```
- 마커가 있으면 그 사이만 교체, 없으면 파일 끝에 추가, 파일이 없으면 생성
- 마커 밖 기존 내용은 바이트 단위로 건드리지 않음 (개행 문자 종류 포함)

### 6.6 파일 작업
- 열기(`.daiso`), 저장, 다른 이름으로 저장
- 스키마 검증 + 오류 위치 표시
- 최근 파일 목록
- 앱 전용 프리셋 라이브러리: `%LOCALAPPDATA%\d-AI-so\presets\*.daiso` → 프로젝트로 복사/적용

---

## 7. 추가 기능 (확정)

| 기능 | 구현 요지 |
|---|---|
| **Context Doctor** | 5.3의 도구별 파일 목록을 로드 순서대로 합쳐 파일별·총 글자 수, 줄 단위 중복, 상반 키워드 충돌 후보를 리포트. 순수 텍스트 처리 |
| **세션 검색** | 모든 세션 jsonl에서 사용자·어시스턴트 텍스트만 추출(도구 결과·시스템·Codex world_state 제외)해 SQLite FTS5 **trigram** 인덱스 구축 → 3글자 이상 부분 문자열 검색, 2글자는 LIKE 폴백. jsonl이 append-only이므로 마지막 바이트 오프셋 기준 증분 갱신. 결과는 프로젝트 > 세션 > 매칭 문장 트리, 클릭 시 resume |
| **세션 Markdown 내보내기** | 같은 파서로 시간순 `**User:** / **Assistant:**` 블록 생성, 도구 호출은 접힌 코드블록, 시스템 이벤트 제외 옵션 |
| **토큰 사용량 통계** | Claude: assistant 줄의 `message.usage`(input/output/cache_creation/cache_read, model) 메시지 날짜별 합산, `<synthetic>` 모델 제외. Codex: `token_count.total_token_usage` 누적값의 마지막 non-null을 세션 시작 날짜에 귀속. 인덱싱 시 함께 집계. 일별 막대, 프로젝트별 상위, 모델별 비율. 비용은 사용자 수정 가능한 단가표로 추정 |
| **CLAUDE.md ↔ AGENTS.md 마이그레이션** | 한쪽만 있으면 생성, 둘 다 있으면 좌우 diff 후 방향 선택. Claude 전용 `@import`는 인라인 전개 + 경고. daiso 마커 블록은 양쪽 동일 유지 |
| settings.json 권한·훅 GUI 편집기 | 후순위(선택). permissions.allow/deny, hooks를 목록 UI로 편집 + 스키마 검증 |

제외: MCP 서버 뷰어, 트레이 만료 알림, Provider 플러그인 구조.

---

## 8. 결정 사항

1. `.daiso` 본문은 **구조형 YAML** (6.3, and/or 트리). 부정은 문장으로 쓴다. 마크다운 양식은 미리보기 전용.
2. Priority: MUST / SHOULD / MAY (생략 시 SHOULD).
3. Codex 규칙 파일은 `AGENTS.md`.
4. 세션 삭제 기본은 휴지통 이동.
5. .NET 8 + Windows App SDK, 우선 unpackaged.
6. 검색 인덱스는 FTS5 trigram (부분 문자열 검색 우선, 인덱스 크기 감수).
7. Codex 아카이브 세션 포함 (배지 표시).
8. CLAUDE.md 연동은 `@PROJECT_RULES.daiso` import, AGENTS.md는 읽기 지시문.
9. 실행 파일은 npm 셸 `claude` / `codex`만 사용.

---

## 9. 프로젝트 구조

```
d-AI-so/
├─ src/
│  ├─ Daiso.App/                  # WinUI 3 (Phase 2)
│  ├─ Daiso.Core/                 # 모델, 순수 인터페이스, YAML 직렬화, 렌더러, 마커, Context 분석
│  ├─ Daiso.Providers.Claude/
│  ├─ Daiso.Providers.Codex/
│  └─ Daiso.Infrastructure/       # SQLite 인덱스, 파일 서비스, 휴지통, 터미널 실행
├─ tools/
│  └─ Daiso.Cli/                  # Phase 1 검증용 콘솔
├─ tests/
│  ├─ Daiso.Core.Tests/
│  ├─ Daiso.Providers.Tests/
│  ├─ Daiso.Infrastructure.Tests/
│  └─ fixtures/                   # 더미 jsonl · 인증 파일 (실제 데이터 금지)
├─ docs/
│  ├─ REQUIREMENTS.md
│  ├─ ARCHITECTURE.md
│  └─ GOAL.md
└─ samples/
   └─ PROJECT_RULES.daiso
```
