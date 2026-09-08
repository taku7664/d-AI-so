# 기능 계획 — 내장 터미널과 채팅방 (2026-09-08)

## 1. 한 줄 요약과 이유

터미널 화면 안에서 Claude Code · Codex · Gemini CLI를 앱을 떠나지 않고 띄우고, 대화를 디스코드처럼 **채팅 블록**으로 본다.
CLI는 숨은 진짜 터미널(ConPTY + WebView2 xterm.js)에서 돌고, 채팅 뷰는 CLI가 쓰는 세션 파일을 실시간으로 읽어 그린다.
이유: 사용자 요청(A) + 다음 기능의 기반(C) — 세션 ID를 우리가 직접 알게 되어 "실행 중" 판정이 정확해지고, 내 프롬프트 드롭다운·`/` 선택기가 붙을 자리가 생긴다.

사용자: 외부 사용자(유료 배포). 결정: `[CONFIRMED]` 전부, 3회차 답 `1:Y / 2:N / 3:일단 진행`.

## 2. 완료 조건 (검증 가능한 문장)

1. 세 CLI가 내장 터미널에서 외부 터미널과 같게 동작한다 — 색·커서·화면 다시 그리기·Ctrl+C 중단·여러 줄 붙이기(bracketed paste).
2. 창 크기를 바꾸면 PTY 크기가 따라가고 CLI가 다시 그린다 (`ResizePseudoConsole`).
3. 터미널 또는 채팅 입력 칸에 포커스가 있으면 앱 단축키(Ctrl+1~7·F5·Esc)가 가로채지 않는다. 선택이 있을 때 Ctrl+C는 복사, 없으면 중단.
4. 채팅 뷰가 메시지를 블록으로 그린다. 블록에 마우스를 올리면 배경이 밝아지고 오른쪽에 `HH:mm`(툴팁에 전체 날짜)과 복사 버튼이 뜬다. 같은 화자의 연속 메시지는 아바타 없이 붙는다. 도구 호출은 접힌 회색 블록, 사용자 메시지는 강조 색 세로줄.
5. 세 도구 모두 CLI가 세션 파일을 쓴 뒤 2초 안에 새 블록이 나타난다. 어시스턴트가 쓰는 동안 "쓰는 중…"이 보인다.
6. 입력 칸에서 Enter는 보내고 Shift+Enter는 줄바꿈이다. `/compact` 같은 슬래시 명령이 CLI에 그대로 전달된다.
7. WebView2 런타임이 없으면 사람 말로 안내하고 외부 터미널로 넘어간다. 설정에 "터미널을 앱 안에서 연다" 스위치가 있다.
8. 방을 여러 개 열고 닫는다. 앱을 닫을 때 살아 있는 방이 있으면 묻는다.
9. 세션 목록의 "이어서 열기"가 방으로 열린다. 요약·세션의 "실행 중"이 우리 프로세스 기준으로 맞는다.
10. 25MB급 출력이 흘러도 UI 스레드 렉이 16ms를 넘지 않는다(출력 읽기는 백그라운드, 채팅 tail은 증분).
11. 빌드 경고 0 · 오류 0, `StringResourceKeysTests` 포함 모든 테스트 통과.

## 3. 영향 범위

**모듈·파일**
- 새 `src/Daiso.Infrastructure/Pty/` — `PseudoConsole`(CreatePseudoConsole·파이프·STARTUPINFOEX·Resize·Close), `PtySession`(읽기 스레드·쓰기·종료 이벤트)
- 새 `src/Daiso.App/Terminal/` — `TerminalHost`(WebView2 + xterm.js 브리지), `Assets/xterm/`(xterm.js·fit 애드온·index.html, MIT, 앱 동봉, `SetVirtualHostNameToFolderMapping`으로 로컬 로드)
- 새 `src/Daiso.App/ViewModels/ChatRoomViewModel.cs`, `ChatBlockViewModel.cs`, `RoomsViewModel.cs`(열린 방 목록)
- 새 `src/Daiso.Infrastructure/SessionTail.cs` — 세션 파일 증분 읽기(Claude·Codex는 오프셋 이어 읽기, Gemini는 리플레이) + `FileSystemWatcher`
- 고침: `TerminalPage`(로비/방 두 상태, 방 탭, 채팅↔터미널 토글, 입력 칸), `TerminalViewModel`, `SessionsPage/ViewModel`(이어서 열기 → 방), `ShellWindow`(단축키 차단·닫기 확인), `AppSettings`(내장 켬끔), `SettingsPage`(스위치), `DashboardViewModel`(실행 중)
- 문구 resw, `ARCHITECTURE §5.3` 다시 쓰기 + `§6.2` 규칙, `REQUIREMENTS`, `UX_SCENARIOS`, `PROGRESS`

**재사용**
- `TerminalCommandBuilder.BuildShell`(pwsh → powershell → cmd, wt 없이) — 내장은 wt를 쓰지 않으므로 셸 명령만 꺼내는 공개 메서드를 하나 연다
- 세 제공자의 `ReadMessagesAsync`·`AppendOnlySessions`, `MessageViewModel`(역할·시각·더 보기·명령 정리), `ToolLook`, `DiscardDialog`, `SelectorBar`/탭 규칙, `FocusRelease`, `SelectorBarVisuals`

**깨질 수 있는 것**
- 터미널 포커스 중 가속기, 앱 종료 시 자식 프로세스, "실행 중"의 이중 판정(파일 추정 vs 프로세스), 1024×700에서 방 배치, 큰 출력 렉, WebView2 없는 PC

**아직 모르는 것 `[TBD]`**
- Gemini가 세션 파일을 턴 중간에도 쓰는지 (스파이크에서 확인)
- Codex 네이티브 Windows의 ConPTY 크기 변경 반응
- WebView2 런타임 미설치 비율 (감지 + 폴백으로 대응)

## 4. 범위

**첫 판**
- ConPTY 래퍼 + 단위 테스트(`cmd /c echo`)
- WebView2 xterm 호스트: 키 가로채기(복사·붙이기·Ctrl+C·앱 단축키 차단), 크기 따라가기, 앱 테마 색
- 채팅 뷰: 세 도구 세션 tail, 블록·호버 시각·복사, 쓰는 중, 도구 호출 접힘
- 입력 칸 → PTY, 방 탭 여러 개, 채팅↔터미널 토글
- 로비/방 두 상태, 이어서 열기 → 방, 외부 터미널은 보조 버튼
- 설정 스위치 + WebView2 감지 → 외부로 자동 전환
- 닫기 확인, 실행 중을 우리 프로세스 기준으로

**뒤로**
- `/` 선택기(Claude `commands`·`skills`, Codex `prompts`, Gemini `commands/*.toml` + 내장 명령표)
- Claude B 프로토콜(stream-json, 스트리밍·승인 카드)
- 터미널 안 검색, 글꼴 크기 설정, 턴 종료 배지·알림, 터미널 탭의 내 프롬프트 드롭다운

**하지 않는 것**
- A 방식에서 승인 프롬프트를 네이티브 카드로 바꾸기(터미널 토글로 처리)
- 세 도구 외 임의 셸 방, 원격 PC

## 5. 순서와 각 단계의 완료 조건

1. **스파이크 — 가장 불확실한 것** (별도 브랜치 아님, `src/Daiso.Infrastructure/Pty` + 임시 `TerminalHost`)
   완료: 앱 안 xterm에서 `claude`가 정상으로 그려지고 Ctrl+C·여러 줄 붙이기·크기 변경이 되며, `~/.claude/projects/...jsonl`이 실시간으로 자라는 것을 확인. Gemini 파일 쓰기 시점 기록. 막히면 여기서 설계 재검토.
2. **핵심 경로 — 방 하나**
   완료: 로비에서 "대화 시작" → 방. 입력 칸 글이 CLI에 들어가고, 답이 블록으로 뜬다(완료 조건 4·5·6). 채팅↔터미널 토글.
3. **연결·정리**
   완료: 방 탭 여러 개·닫기, 이어서 열기 → 방, 설정 스위치·WebView2 폴백, 가속기 차단, 앱 닫기 확인, 실행 중 판정(완료 조건 3·7·8·9).
4. **테스트·문서**
   완료: PTY·tail·브리지 메시지 단위 테스트, 완료 조건 10·11, `ARCHITECTURE §5.3`, UX 시나리오, PROGRESS.

각 단계마다 커밋 + `git push origin main`.

## 6. 지키는 제약

- 네트워크 호출 없음. xterm.js는 앱에 파일로 동봉하고 로컬 가상 호스트로만 로드
- 인증 토큰·세션 파일은 읽기 전용. 터미널 출력·로그·예외에 토큰 값을 남기지 않는다(PTY 출력은 저장하지 않는다)
- 기존 외부 터미널 실행 경로(`WindowsTerminalLauncher`)·`tools/run-app.ps1`·`StringResourceKeysTests` 규칙 유지
- 최소 창 1024×700, 세션 상세 열 400 고정 폭
- Claude Code는 Git Bash를 요구하므로 도구별 셸 선택은 기존 `BuildShell` 규칙을 따른다
- 검수 자동화는 UIA만. 마우스 이동·클릭 자동화 금지

## 7. 결정 기록과 미정

| 결정 | 상태 |
|---|---|
| 엔진: PTY + 세션 파일 tail 먼저, Claude만 B(stream-json) 나중 | `[CONFIRMED]` |
| 기본 열기를 내장 채팅으로, 외부 터미널은 보조 | `[CONFIRMED]` |
| 설정 스위치 + WebView2 없으면 자동 외부 | `[CONFIRMED]` |
| 블록 모양(아바타·연속 붙임·호버 시각·복사·접힌 도구 호출·사용자 세로줄) | `[CONFIRMED]` |
| 레이아웃: 로비/방 두 상태, 채팅↔터미널 토글로 같은 자리 | `[PROPOSED]` "일단 진행" — 방 화면이 서면 다시 본다 |
| 앱 닫기 시 살아 있는 방: 묻는다(끄기·취소) | `[PROPOSED]` |
| 스트리밍 표시: A에서는 "쓰는 중…"만, 글자 단위는 B에서 | `[CONFIRMED]` 한계로 인지 |
| Gemini 파일 쓰기 시점, Codex 크기 변경 반응 | `[RESOLVED]` 아래 스파이크 결과 |

### 스파이크 결과 (2026-09-08)

- **의사 콘솔 + xterm 이 앱 안에서 동작한다.** Claude·Gemini·Codex 셋 다 TUI가 그대로 그려진다(색·박스·커서·선택 강조·구문 강조). Gemini의 "이 폴더를 신뢰?" 승인 화면도 정상으로 뜬다 — A 방식의 터미널 토글로 승인·슬래시를 처리한다는 계획이 맞다.
- **종료 시 힙 손상**을 잡았다. `TerminateProcess`는 루트 pwsh만 죽여 자식(node)이 콘솔 출력 쪽을 잡고 있어 읽기가 안 풀렸고, 파이프 핸들을 손으로 닫다 네이티브 ReadFile과 겹쳐 힙이 깨졌다. 프로세스 **트리 전체**를 kill 하고 IO를 `FileStream`(핸들 수명 관리)으로 바꿔 해결했다. 격리 테스트로 세 CLI가 깨끗이 종료됨을 확인했다.
- **중첩 세션 표식**을 걷어낸다. 이 앱 자체가 Claude Code 세션 안에서 돌면 `CLAUDECODE`·`CLAUDE_CODE_*`·프록시 주소가 새어 안의 CLI가 하위 세션으로 오작동한다. `PtyEnvironment`가 표식을 지우고 사용자 변수는 남긴다.
- **세션 파일 tail 주기.** Gemini는 실행 즉시 세션 파일을 새로 쓴다(패치 로그, 턴마다 갱신). Codex는 첫 사용자 턴이 있어야 rollout 파일을 쓴다. 둘 다 파일 tail로 채팅 블록을 만들 수 있다.
- **아직 확인 못 한 것**: WebView2 xterm → 브리지 → PTY로 가는 **키 입력**을 자동화로 검증하지 못했다(SendKeys가 WebView2에 안정적으로 들어가지 않고, 마우스 자동화는 금지). 단위 테스트에서 PTY 쓰기(입력)는 확인됐다. 사람이 임베디드 터미널에 직접 타이핑해 확인이 필요하다.

## 진행 (2026-09-08)

- **1단계 스파이크 완료** — 위 스파이크 결과 참고.
- **2단계 핵심 경로(방 하나) 완료** — `SessionTail`(생성 시각으로 활성 파일 식별, 증분 전달, 단위 테스트 2건), `ChatRoomViewModel`·`ChatBlockViewModel`, 터미널 화면에 방 UI(제목·토글·채팅 블록 목록·입력 칸). 방 UI는 UIA로 렌더 확인. **남은 라이브 확인**: 자식 CLI가 한 턴을 마쳐 블록이 뜨는 것 — 이 환경은 앱을 같은 저장소의 Claude 세션 안에서 구동해 세션 폴더가 겹치고, 자식 CLI의 신뢰 프롬프트를 자동화가 넘기지 못해 사람이 직접 확인해야 한다.
- **3단계 일부 완료** — 설정에 "터미널을 앱 안에서 연다" 토글(WebView2 없으면 잠기고 외부로), 토글이 꺼졌거나 런타임이 없으면 "여기서 열기"가 외부 터미널로 폴백, 앱을 닫을 때 살아 있는 방이 있으면 확인 대화 후 자식 프로세스 트리 정리(App의 방 등록부 + AppWindow.Closing). 앱 단축키는 xterm 키 핸들러가 Ctrl+1~9·F5를 먹어 터미널 포커스 중 가로채지 않는다.
- **세션 "이어서 열기" → 방 완료** — 요약·세션의 "이어서 열기"가 내장이 켜져 있으면 터미널로 이동해 그 도구·폴더·resume 인자로 방을 자동으로 연다(꺼져 있거나 WebView2 없으면 외부). 화면 로드 때 탭 기본 선택·TwoWay 바인딩이 도구·인자를 덮어써서, 자동 열기를 소비할 때 도구를 다시 고르고 인자를 다시 심는다. UIA로 Claude 세션 이어서 열기 → Claude 탭 + `claude --resume <id>` 확인.
- **방 탭 여러 개 완료** — `RoomManager`(싱글턴)가 열린 방을 들고 있어 화면을 옮겨도 방이 산다. 터미널 화면에 탭 띠(도구 아바타·폴더 이름·닫기 X), 탭을 누르면 하나뿐인 WebView2 호스트를 그 방으로 다시 가리키고 버퍼를 되돌려 화면을 복원한다(방이 출력을 8MB까지 버퍼링). 앱 종료 확인·정리도 RoomManager로. UIA로 방 2개 + 화면 이동 후 유지 확인.
- **다음(3단계 마지막)**: 요약·세션의 "실행 중"을 `RoomManager.IsRunning`으로.

## 8. 첫 작업

`src/Daiso.Infrastructure/Pty/PseudoConsole.cs` + `PtySession.cs`를 만들고 `cmd /c echo` 테스트로 읽기·쓰기·종료를 확인한다.
이어서 `Daiso.App/Terminal/TerminalHost`(WebView2 + 동봉 xterm.js)에 PTY를 물려 터미널 화면 임시 영역에서 `claude`를 띄운다.
확인 항목: 화면 그리기 · Ctrl+C · 여러 줄 붙이기 · 크기 변경 · 세션 파일 실시간 증가 · Gemini 파일 쓰기 시점.
