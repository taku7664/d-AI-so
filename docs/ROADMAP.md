# 단계 계획

Electron 전환을 Stage 0~8로 나누고, 단계마다 할 일과 완료 기준을 정한다.
왜 이렇게 하는지는 [DECISIONS.md](DECISIONS.md), 일하는 방식과 진행 상황은 [README.md](README.md)에 있다.

## Stage 0 — backend 솔루션 뼈대

- `backend/`에 `Daiso.sln`, `Directory.Build.props`, `global.json`을 만든다
  - `global.json`은 `old/global.json` 값을 그대로 쓴다 (SDK 10.0.400, `latestMinor`). dotnet은 **현재 폴더**에서 `global.json`을 찾으므로 명령은 `backend/` 안에서 돌린다
  - `Directory.Build.props`는 `old/Directory.Build.props`의 `Nullable`·`TreatWarningsAsErrors`·`InvariantGlobalization` 등을 그대로 가져온다. **`Version`은 Stage 8까지 `old/Directory.Build.props` 하나가 정본이다** (`old/tools/make-installer.ps1`이 거기서 읽는다)
  - `old/src`의 프로젝트는 자기 위쪽의 `old/Directory.Build.props`를 따른다. 새 props는 `backend/` 아래 새 프로젝트에만 걸린다
- 새 솔루션이 `old/src`의 백엔드 프로젝트와 `old/tests`의 백엔드 테스트 세 벌을 참조한다. `Daiso.App`은 넣지 않는다
- `old/tools/run-app.ps1`로 옛 앱이 여전히 뜨는지 확인한다
- **완료 기준:** `backend/`에서 `dotnet build` · `dotnet test` 초록, `old/`에서도 초록

## Stage 1 — 서버 뼈대와 보안

- Host TFM은 `net10.0-windows`다 ([DECISIONS.md](DECISIONS.md) "정한 것"). `Infrastructure`가 `net8.0-windows`라서 Host도 `-windows` TFM이어야 참조할 수 있다
- `backend/src/Daiso.Host/` (ASP.NET Core minimal API). DI 구성과 UI 의존 없는 서비스를 옮긴다 ([MIGRATION_MAP.md](MIGRATION_MAP.md))
- [SECURITY.md](SECURITY.md)의 일곱 가지 전부, 그리고 서버를 띄우는 순서
- **Electron 없이 혼자 뜰 수 있어야 한다.** Stage 1에는 Electron이 없다. 토큰 환경 변수가 없으면 Host가 직접 토큰을 만들고, 열 주소를 표준 출력과 `server.json`에 남긴다 ([SECURITY.md](SECURITY.md))
- 자료 폴더는 옛 앱과 같은 `%LOCALAPPDATA%\DAIso`를 쓴다 ([DECISIONS.md](DECISIONS.md) "정한 것"). 옛 앱의 인덱스는 쓰기 잠금이 프로세스 안에만 있다(`SqliteSessionIndex`의 `_writeGate`). 두 인스턴스가 같은 파일을 동시에 다시 만들어도 깨지지 않는 것을 테스트로 확인했다(2026-10-05). 테스트 자료가 작아 잠금이 짧은 경우만 봤으므로, 실제 크기 인덱스는 Stage 4에서 다시 본다
- `GET /api/health`, OpenAPI 문서, 공용 WebSocket `/ws`
- `backend/tests/Daiso.Host.Tests/`: 보안 규칙을 하나씩 깨 보는 테스트
- **완료 기준:** 보안 테스트 초록. 크롬에서 토큰 주소로 열면 health가 보이고, 토큰 없이 열면 거절된다
  - 2026-10-06 완료: 토큰 없이 열면 거절된다(`/api/health` 401, 크롬은 오류 화면). 토큰 주소로 여는 쪽은 Stage 2의 Electron 창(Chromium)으로 확인했다. 토큰 주소로 연 창이 토큰을 쿠키로 바꾼 뒤 토큰 없는 주소에서 첫 화면(`DAIso Host`)을 보여 준다

## Stage 2 — Electron 껍데기

- `frontend/desktop/` (Electron 메인 + preload, TypeScript). `frontend/package.json`의 workspace로 묶는다
- 서버를 자식 프로세스로 띄우고 주소를 읽어 창에 연다. 앱이 끝나면 서버도 끈다
- 두 번째 실행 막기, 트레이, 창 X → 트레이로 숨기기, 트레이 메뉴 "브라우저로 열기"
- 새 앱을 띄우는 `tools/run-app.ps1`을 루트에 새로 만든다 (옛 것은 `old/tools/`에 그대로)
- **완료 기준:** 빌드한 Electron 앱이 서버를 띄우고 빈 화면을 보여 준다. 앱을 끄면 서버 프로세스가 남지 않는다. `npm run lint` 초록
  - 2026-10-06 완료. Electron 44.5.1. Host를 잡 오브젝트 밖에서 띄워야 끌 때 `server.json`까지 정리된다 ([frontend/desktop/README.md](../frontend/desktop/README.md))

## Stage 3 — 화면 뼈대와 탭 계약

- `frontend/web/` (Vite + React + TS). workspace로 묶는다. [D안 시안](design/README.md)의 위 줄·왼쪽 메뉴·색·글꼴을 CSS 변수로 옮긴다
  - 테마는 시스템 · 밝게 · 어둡게. 색은 CSS 변수 한 벌씩
  - 글꼴(Gothic A1 · Hahmlet · JetBrains Mono, OFL-1.1)은 파일로 동봉한다. 아이콘은 Bootstrap Icons(MIT). 라이선스는 Stage 8에서 배포 문서에 적는다
- 위 줄: 프로젝트 선택기(`Ctrl+P`), 종, 계정 단추, 톱니. 이 단계에서는 프로젝트 선택기만 실제로 돌고 나머지는 자리만 둔다
- 공용 경로 `/api/projects` ([ARCHITECTURE.md](ARCHITECTURE.md) "탭에 속하지 않는 공용 경로"). 마지막에 고른 프로젝트를 설정에 남긴다
- [frontend/web/src/tabs/README.md](../frontend/web/src/tabs/README.md)·[backend/src/Daiso.Host/Tabs/README.md](../backend/src/Daiso.Host/Tabs/README.md)의 탭 약속, 라우팅, `Ctrl+1~6` 단축키. 설정은 메뉴에 없고 톱니로 연다
- OpenAPI → TS 클라이언트 생성, `/ws` 알림 → 캐시 무효화(TanStack Query)
- 문구를 `frontend/web/src/strings/ko.json`으로 옮기고 키 검사 vitest를 만든다
- **완료 기준:** 여섯 탭과 설정이 빈 화면으로 뜨고 메뉴·단축키·톱니로 오간다. 프로젝트를 바꾸면 위 줄과 탭이 따라 바뀐다. 크롬 탭과 Electron 창에서 똑같이 보인다
  - 2026-10-06 코드 끝: `frontend/web`, Host가 화면 빌드를 내주고(`DAISO_WEB_ROOT`) 탭 주소로 새로 열어도 화면을 낸다, `/api/projects`, OpenAPI 스냅숏 시험 → TS 타입, `/ws` → 캐시 무효화, 문구 키 검사. Electron 창에 화면이 뜨는 것(창 제목 `요약 · DAIso`)까지 확인했다
  - 2026-10-06 Electron 창 확인 끝: 메뉴 여섯 개, `Ctrl+1~6`, 톱니(누르면 설정, 다시 누르면 돌아감), 뒤로·앞으로, 탭 주소로 새로 열기, `Ctrl+P` → 방향키·Enter로 프로젝트 바꾸기(위 줄 경로와 탭이 따라 바뀜), Esc로 닫기, 다시 켜도 고른 프로젝트 유지. 화면 오류 없음. 방법은 [frontend/desktop/README.md](../frontend/desktop/README.md) "창을 스크립트로 눌러 보기"
  - 하다가 고친 것: `Ctrl+P`로 열면 방향키가 안 먹던 것(초점), 인덱스 DB가 깨졌을 때 `/api/projects`가 500을 내던 것(최근 폴더로 물러서게)
  - 크롬 탭 확인은 저장소 주인이 틈틈이 한다. 토큰을 다뤄야 해서 AI는 하지 않는다
  - 인덱스를 새로 고치는 일(옛 `IndexService`)은 아직 옮기지 않았다. 프로젝트 목록은 지금 있는 인덱스를 읽기만 한다. Stage 4에서 옮긴다

## Stage 4 — 사용량 탭

- 첫 수직 조각이다. 읽기만 하는 화면이라 서버·타입 생성·캐시·알림 흐름을 처음 끝까지 꿰기 좋다
- 인덱스를 처음 여는 탭이다. 옛 앱과 Host를 동시에 띄우고 실제 크기 인덱스로 둘이 같이 갱신할 때 한쪽이 실패하지 않는지 본다 (Stage 1에서는 작은 테스트 자료로만 봤다)
- 토큰만 보여 준다. 단가표와 $ 추정은 옮기지 않는다 ([DECISIONS.md](DECISIONS.md) "사용량은 토큰과 구독 한도만")
- "이 프로젝트 / 모든 프로젝트"로 바꿔 본다
- 구독 한도 (`/api/limits`). 탭을 열 때 비동기로 읽고, 파일이 바뀌면 `/ws`로 알려 바로 반영하고, 기준 시각을 붙인다
  - Codex: 세션 기록의 `rate_limits`(`primary`·`secondary`의 `used_percent`·`window_minutes`·`resets_at`)를 `Providers.Codex`에서 읽는다. 백엔드 공용 코드라 `old/` 솔루션 테스트도 돌린다
  - Claude: 상태줄 입력의 `rate_limits`(`five_hour`·`seven_day`)를 파일로 남기는 DAIso 상태줄 스크립트. "한도 보기 켜기"로 동의를 받아 `~/.claude/settings.json`에 등록하고, 끄면 원래대로 돌린다. 쓰던 상태줄이 있으면 감싸서 그대로 보이게 한다
  - 초기화 시각이 지나면 화면에서 "초기화됨 · 0%"로 보여 준다
- **완료 기준:** 옛 사용량 화면과 토큰 수가 같다 (같은 인덱스로 나란히 띄워 비교). Codex 한도가 세션 기록 값과 같다. Claude 한도 보기를 켜고 끄면 `~/.claude/settings.json`이 원래대로 돌아온다
  - 2026-10-06 토큰 부분 끝: `/api/usage`(일·주·월, 도구·프로젝트로 좁히기), `/api/tools`, `/api/index`(뜰 때 한 번 갱신, 다시 읽기), 사용량 화면. 인덱스에 프로젝트로 좁히는 질의를 더했다(`ISessionIndex.GetUsageAsync` 오버로드, `old/` 테스트 포함). 화면 숫자를 같은 인덱스에 SQL로 직접 센 값과 맞췄다(오늘·7일·30일·모델별). 옛 앱 화면을 띄워 나란히 본 것은 아니다. 옛 화면도 같은 `GetUsageAsync`를 쓰고, 다른 점은 빈 날을 0 칸으로 채우는 것뿐이다
  - 알아 둘 것: 합계에 캐시 읽기가 들어가 숫자가 크다(30일 79B 중 61B가 캐시 읽기). 옛 화면과 같은 셈이다
  - 2026-10-06 구독 한도 코드 끝: `/api/limits`, Codex 세션 기록 읽기(`Providers.Codex`의 `CodexRateLimits`), Claude 상태줄 명령 `backend/src/Daiso.StatusLine`과 켜고 끄기, 파일이 바뀌면 `/ws`로 알림, 사용량 맨 위 구독 한도 카드. Codex 값(63%, 10-05 16:03)을 세션 기록 전체를 직접 훑은 값과 맞췄다. Claude 켜고 끄기는 임시 홈으로 시험했다(원래 설정 그대로 돌아옴, 감싼 상태줄 출력 유지, 등록된 명령을 cmd·Git Bash로 실제 실행)
  - 2026-10-06 완료 (주인 허락): 진짜 `~/.claude/settings.json`으로 화면에서 켜고 끄기. 처음에는 내용은 같은데 CRLF로 바뀌고 끝 줄 바꿈이 빠져서, 켠 사이 다른 칸을 안 바꿨으면 켜기 전 파일을 바이트 그대로 돌리게 고쳤다(SHA-256 같음 확인). 옛 앱을 같이 띄워 사용량 화면을 나란히 봤다: 날마다 토큰과 프로젝트 상위 10이 같고, 다른 점은 기록 없는 날(9/18)을 새 화면만 0 칸으로 보여 주는 것뿐이다. 두 앱이 같은 인덱스를 같이 갱신해도 오류가 없고 `quick_check` ok
  - 확인 못 한 것: Claude가 실제로 한도를 보내 파일이 생기는 것. 이 세션에서 띄운 CLI는 데스크톱 세션의 환경 변수를 물려받아 "API 과금·로그인 만료"로 떠서 `rate_limits`가 오지 않는다(구독자에게만 온다). 주인 PC에 켜 두었으니 평소 CLI를 한 번 쓰면 사용량 탭에 들어온다
- **여기서 C# 서버를 계속 갈지 판단한다** ([DECISIONS.md](DECISIONS.md) "정한 것"의 버린 안)

## Stage 5 — 요약 · 세션 탭

- 요약: 고른 프로젝트의 열린 터미널, 최근 세션(줄에서 바로 "이어서"), 손볼 것, 숫자. "모든 프로젝트"면 프로젝트 카드
- 계정 단추(`/api/accounts`): 로그인 상태, 다시 로그인, 저장한 계정. 옛 요약의 계정 카드가 여기로 온다
- 세션: 검색(FTS5), 필터, 대화 보기, 이어서 열기, Markdown 내보내기, 휴지통으로 보내기(영구 삭제는 체크박스), 실행 중 세션 지우기 차단
- 세션 이름 붙이기. 자료 폴더의 `session-names.json`에 따로 둔다
- 정리 기준(며칠·몇 MB)은 "골라 체크" 안에서 바꾼다. 요약의 "손볼 것"도 같은 값을 쓴다
- 2026-10-06 세션 탭(5a) 끝: `/api/sessions`(목록·필터, 검색, 대화, 내보내기, 이름, 지우기, 이어서 열기, 정리 기준)와 화면. 세션을 경로로 받는 요청은 인덱스에 있는 세션일 때만 그 파일을 만진다(시험으로 확인). 이어서 열기는 지금은 바깥 터미널 창이고 Stage 6에서 앱 안 터미널로 바꾼다. Electron 창에서 목록 570건, 대화 보기, 검색, 이름 붙였다 떼기, 지우기 대화상자(실행 중 세션 제외)를 눌러 봤다. 내보내기 저장 창은 OS 대화상자라 눌러 보지 않았다
- 2026-10-06 요약·계정(5b) 끝: `/api/dashboard`(프로젝트 첫 화면: 최근 세션 5, 숫자, 손볼 것 / 모든 프로젝트: 프로젝트 카드), `/api/accounts`(로그인 상태, 다시 로그인, 저장한 계정 저장·바꾸기·지우기)와 위 줄 계정 단추. 저장한 계정은 Host 자료 폴더 아래 `profiles`(실제로는 옛 앱과 같은 곳, 시험은 임시 폴더). 응답에 토큰 값이 없는 것을 시험으로 확인. "모든 프로젝트"에서는 손볼 것을 카드 위에 둔다(카드가 65개라 아래 두면 안 보였다). 열린 터미널 칸은 Stage 6에서 채운다
- 2026-10-06 프로젝트 목록 다듬기(5c) 끝: 워크트리(Claude `.claude\worktrees\…`, Codex `~\.codex\worktrees\{id}\{폴더}`)를 원래 프로젝트로, Claude 데스크톱 임시 폴더(`scratch-workspaces`)를 "Claude 임시 작업 공간" 하나로 묶는다(`Shared/ProjectGroups.cs`). 묶는 것은 화면에 보여 줄 때만이고 인덱스는 그대로다. 프로젝트로 좁힐 때(세션·사용량·요약) 묶인 폴더까지 넣는다. 이름이 같은 프로젝트는 상위 폴더까지 붙여 가른다(`PathLabels`). 프로젝트를 모르던 세션 29개는 Claude 세션 메타를 앞 20줄에서만 찾던 탓이었다. 데스크톱 앱 세션은 cwd 가 23번째 줄에 나온다. 500줄까지 찾게 고치고(`old/` 테스트 포함) 인덱스를 다시 만들었다. 결과: 프로젝트 65 → 43, 프로젝트 모름 29 → 2
- **완료 기준:** 옛 세션 화면의 기능 목록을 하나씩 대조해 빠진 것이 없다

## Stage 5d — 요약 다시 (2026-10-07)

Stage 5 를 끝냈다고 했지만 요약이 D안과 달랐고(카드에서 터미널 상태가 빠지고 시안에 없는 누적 숫자를 넣었다), D안의 요약 자체도 실제 데이터로는 폴더 목록이었다. 시안을 다시 정했다: [design/summary.html](design/summary.html)

- 요약: 타일 넷(열린 터미널 · 오늘 세션 · Codex 7일 한도 · 이번 주 토큰), 최근 세션 최대 5개, 워크트리 현황
- 종 팝업: 열린 터미널, 마지막으로 하던 것(이어서 하기), 다른 프로젝트에서 하던 것(프로젝트마다 가장 최근 것)
- 최근 세션의 "마지막으로 친 질문": 인덱스 메시지에서 고른다. `<task-notification>`·`<system-reminder>`·`/compact` 결과·"This session is being continued…" 같은 사람이 치지 않은 줄은 건너뛴다
- 인덱스 실시간 갱신: 지금은 앱을 켤 때와 "다시 읽기" 단추로만 읽는다. Claude·Codex 세션 폴더를 지켜보다 바뀐 파일만 이어 읽고 `/ws`로 알린다
- 워크트리: Host 가 프로젝트 저장소마다 `git worktree list`를 읽는다. 메인 작업 폴더는 뺀다
- 프로젝트를 고르면 같은 틀을 그 프로젝트로 좁힌다(타일·최근 세션·워크트리). 한도와 계정은 계정 단위라 그대로
- "손볼 것"(로그인 만료 · 정리할 세션 · 지침 못 읽음)은 종 팝업 맨 위. 팝업이 창보다 길어지면 팝업 안에서 스크롤
- **완료 기준:** 시안과 실제 앱 화면을 1280·960, 밝게·어둡게로 찍어 칸마다 대조한 결과를 붙인다
- 2026-10-07 코드 끝: `/api/dashboard`(타일·최근 세션·마지막 질문)와 `/api/dashboard/worktrees`, `/api/bell`, 세션 폴더 지켜보기(`IndexWatcher`, 바뀐 파일만 이어 읽기 — `old/` 인덱스·도구에 `RefreshFilesAsync`·`ReadSessionMetaAsync`·`GetLatestUserMessagesAsync`를 더하고 시험 포함). 실제 앱(Electron)을 1280·960, 밝게·어둡게, 종 팝업 연 상태로 찍어 시안과 대조했다. 대조하다 고친 것: 최근 세션의 프로젝트 이름표가 워크트리 폴더 이름으로 나오던 것(원래 프로젝트 이름으로), 시각이 날짜 전체로 나오던 것("오늘 01:23"). 요약의 가장 최근 세션 시각이 실제 파일 시각과 8초 안으로 따라오는 것을 확인
- 2026-10-07 남은 것 셋 끝: Codex 가 사람 글 앞에 붙이는 머리말(IDE 맥락·첨부 파일·리뷰 댓글, "## My request for Codex:" 앞)을 벗겨 제목·마지막 질문에 사람 글만 보인다(머리말이 200자에서 잘린 제목은 "첫 질문 없음"). 제목이 없는 세션(이어 붙인 세션)은 요약에서 마지막 질문을 제목 자리에 둔다. 터미널 방에 서버가 정한 프로젝트(`RoomInfo.Project`, 워크트리면 원래 저장소)를 붙여 워크트리 폴더에서 연 방도 그 프로젝트 방으로 묶인다. 실제 앱 화면으로 확인

## Stage 6 — 터미널 탭

시작 전에 방의 "허락 기다림"을 알아낼 수 있는지 조사한다 ([DECISIONS.md](DECISIONS.md) "미정").

### 6a — 진짜 터미널

- 서버 `/api/terminal/rooms/{id}/pty`(WebSocket) ↔ xterm.js. 방 목록, 새 터미널 단계 카드, 설치 버튼, 외부 터미널로 열기
- 방 이름. 고른 프로젝트의 방만 보이고, 새 터미널은 폴더가 지금 프로젝트로 채워져 있다
- 위 줄 종: 모든 프로젝트의 방 상태(작업 중 · 답 끝남 · 허락 기다림). 누르면 그 방으로 간다
- Electron 창에서는 파일 끌어 놓기 → 경로 입력, 클립보드 이미지 붙여넣기
- **옛 터미널 쪽 fix 커밋(30개)을 훑어 같은 문제가 다시 나지 않는지 하나씩 확인한다**
- **완료 기준:** Claude·Codex 방을 열고, 앱을 트레이로 숨겼다 다시 열어도 출력이 이어진다
  - 2026-10-06 코드 끝: 방 열기·닫기·이름·본 표시, 화면 WebSocket(여러 화면이 같은 방을 같이 볼 수 있다, 다시 붙으면 지난 출력을 통째로), 상태 훅(위 "정한 것"), 새 터미널 카드(폴더는 지금 프로젝트로 채워짐, 모델·인자, 새 창에서 열기), 위 줄 종(다른 프로젝트 방이면 그 프로젝트로 바꿔 연다), 요약의 열린 터미널, 세션·요약의 "이어서"가 앱 안 방으로. 시험: cmd.exe 를 띄우는 시험 도구로 진짜 ConPTY·입출력·다시 붙기·훅 상태·Origin 거절
  - 실제로 본 것: Claude 방이 뜨고 질문을 보내면 훅이 "작업 중"을 남긴다. Codex 방은 "작업 중 → 답 끝남(안 봄)"과 종 숫자 1까지 확인
  - 하다가 찾은 것: 동봉 ConPTY(OpenConsole)가 안 쓰이고 Windows 내장 conhost 가 돌았다. conpty.dll 이 `runtimes/win-x64/native`에서 실리는데 OpenConsole 은 출력 폴더 바로 아래에만 있어서였다. Host 빌드가 옆에도 두게 고쳐 OpenConsole 이 뜨는 것을 확인. 방 환경에서 `DAISO_*`도 걷는다(`old/` PtyEnvironment, 시험 포함)
  - 남은 것: 앱을 트레이로 숨겼다 다시 열어 출력이 이어지는지 눈으로 확인. Claude 답 끝남·허락 기다림은 주인 PC 의 Claude CLI 로그인이 만료돼("Login expired") 끝까지 보지 못했다

### 6b — 말풍선 보기

- 같은 방을 말풍선으로 본다. 세션 기록을 읽어 그리고(옛 `SessionTail`), CLI는 뒤에서 그대로 돈다
- 입력칸의 글은 그 방의 CLI 입력 줄로 보낸다. `/`를 치면 명령·스킬 목록. 옛 `SlashCommandReader`가 못 읽는 프로젝트 스킬·플러그인 스킬·`user-invocable` 표시를 더한다
- 화면 없이는 안 되는 명령(`/login`·`/permissions` 등)은 고르면 터미널 보기로 넘긴다
- **완료 기준:** 말풍선 보기에서 보낸 글과 `/` 명령이 터미널 보기에서 친 것과 같은 결과를 낸다

## Stage 7 — 내 규칙 · 내 프롬프트 · 설정

- 규칙·프롬프트는 **폴더가 원본**이다. 서버가 FileSystemWatcher로 감시하고 바뀌면 `/ws`로 알린다
- 지침은 지금 프로젝트의 것을 연다. 폴더를 따로 고르지 않는다. 지침을 읽는 도구를 보여 주고, 못 읽는 도구가 있으면 "읽게 하기"로 불러오는 줄을 넣는다
- 프롬프트는 "{프로젝트}에 넣기" 한 번으로 넣고, 넣은 뒤 "새 터미널에서 시작"으로 잇는다
- 설정은 톱니로 연다. 단가표와 정리 기준은 없다
- 폴더 위치를 설정에서 고를 수 있게 한다. 기본값은 옛 `AppPaths`의 `presets`·`prompts`다
- 시작 전에 폴더가 겹칠 때의 우선순위를 정한다 ([DECISIONS.md](DECISIONS.md) "미정")
- **완료 기준:** 탐색기나 AI가 폴더의 파일을 고치면 화면이 저절로 바뀐다

## Stage 8 — 배포 전환과 old 제거

- `old/docs/RELEASE.md`를 `docs/`로 옮기고 §2 표를 먼저 고친다. Windows App SDK·WebView2 줄을 빼고 Electron 줄을 넣는다
- 설치판에 `Daiso.StatusLine.exe`를 Host 옆에 같이 넣는다. 사용자 Claude 설정에 그 경로가 적히므로 설치 경로가 바뀌면 다시 등록해야 한다
- 시작 전에 설치 프로그램 도구를 정한다 ([DECISIONS.md](DECISIONS.md) "미정")
- 백엔드 프로젝트와 테스트를 `old/src`·`old/tests`에서 `backend/src`·`backend/tests`로 `git mv`한다
- 필요한 옛 문서를 `docs/`로 옮긴 뒤 `old/`를 지운다
- **완료 기준:** RELEASE §5 확인 목록을 깨끗한 Windows 10 Home에서 통과한다

## 위험과 되돌리기

| 위험 | 대응 |
|---|---|
| ViewModel 로직을 옮기다 동작이 어긋난다 | 옮기기 전에 그 로직을 Core/Infrastructure로 내리고 테스트부터 붙인다. 옛 앱과 새 앱을 같은 인덱스로 나란히 띄워 비교한다 |
| 보안 규칙이 빠진 엔드포인트가 생긴다 | 검사를 미들웨어 한 곳에 둔다. 보안 테스트가 모든 경로를 돈다 |
| 설치 파일이 커진다 (Chromium 동봉) | 받아들인다. 대신 WebView2 부트스트래퍼와 Windows App SDK가 빠진다 |
| 서버 프로세스가 고아로 남는다 | 서버가 부모 프로세스를 지켜보다 스스로 끝난다. Stage 2 완료 기준에 넣었다 |
| C# 서버에서 막힌다 | Stage 4에서 판단한다 |
| 새 서버를 위해 백엔드를 고치다 옛 앱이 깨진다 | 백엔드를 고친 커밋은 `old/` 솔루션 빌드·테스트도 돌린다 |
| 사용자 인덱스가 깨져 있었다 (2026-10-06 발견). `index.db` 본파일(09-14)은 멀쩡하고 `index.db-wal`(10-05 02:38)에서 `messages_fts_docsize`가 깨졌다. 원인은 모른다 | 주인 허락을 받아 깨진 세 파일을 `%LOCALAPPDATA%\DAIso\index-broken-20261006\`로 옮기고 Host와 같은 서비스 구성으로 다시 만들었다(세션 570, `integrity_check` ok). Stage 4에서 옛 앱과 동시 갱신을 볼 때 원인 후보로 같이 본다 |
| 옛 앱과 새 앱이 같은 자료 폴더(`index.db`, `settings.json`)를 동시에 쓴다 | 설정은 한쪽에서만 바꾼다. 인덱스 동시 쓰기는 Stage 1에서 확인한다. Stage 8에서 옛 앱을 지우면 없어지는 위험이다 |
| 되돌리기 | Stage 7까지 `old/`가 그대로 있으므로 언제든 멈출 수 있다. Stage 8이 되돌리기 어려운 단계다 |
