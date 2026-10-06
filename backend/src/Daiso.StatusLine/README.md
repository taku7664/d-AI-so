# Daiso.StatusLine

Claude Code 상태줄 명령이다. Claude가 응답할 때마다 이 실행 파일을 띄우고 stdin으로 JSON을 넘긴다. 그 JSON에서 구독 한도(`rate_limits`)만 골라 파일로 남기면 `Daiso.Host`가 읽어 사용량 탭에 보여 준다.
왜 이렇게 하는지는 [docs/DECISIONS.md](../../../docs/DECISIONS.md) "구독 한도는 도구가 남긴 파일로만 읽는다"에 있다.

## 하는 일

1. stdin을 다 읽는다
2. `rate_limits`가 있으면 `{자료 폴더}\limits\claude.json`에 `{ "at": 받은 때, "rate_limits": 받은 그대로 }`를 쓴다. 임시 파일에 쓰고 자리를 바꾼다
   - `rate_limits`가 없으면(구독자가 아니거나 첫 응답 전) 아무것도 쓰지 않는다. 마지막 값을 지우지 않는다
3. `{자료 폴더}\limits\claude-statusline-original.json`이 있으면 사용자가 원래 쓰던 상태줄 명령을 같은 입력으로 돌려 그 출력을 그대로 내보낸다(감싸기). 2초 안에 안 끝나면 끊는다
   - 셸은 `CLAUDE_CODE_GIT_BASH_PATH`의 bash, 없으면 PATH의 `bash`, 그것도 없으면 `cmd.exe`다. Windows의 Claude Code는 Git Bash로 명령을 돌린다
4. 무슨 일이 있어도 조용히 0으로 끝난다. 여기서 실패해도 Claude 화면이 깨지면 안 된다

로그인 토큰은 보지도 쓰지도 않는다. stdin에 토큰은 오지 않는다.

## 터미널 방 상태 훅

`--hook {run|done|ask|codex} --room {방 id} --data {자료 폴더}`로 부르면 상태줄 일은 하지 않고 `{자료 폴더}\rooms\{방 id}.json`에 `{ "state": …, "at": … }`를 남긴다. Host 가 방을 띄울 때 Claude 는 `--settings`의 hooks 로, Codex 는 `-c notify=[...]`로 넣는다(`backend/src/Daiso.Host/Tabs/Terminal/RoomService.cs`). `codex`는 Codex 가 마지막 인자로 붙이는 JSON 의 `type`이 `agent-turn-complete`일 때만 `done`을 남긴다. 방 id 가 32자 16진수가 아니면 아무것도 쓰지 않는다(경로로 쓰기 때문).

## 등록

`Daiso.Host`가 사용량 탭의 "한도 보기 켜기"를 받으면 `~/.claude/settings.json`의 `statusLine`을 이렇게 바꾼다(`Shared/Limits.cs`의 `ClaudeStatusLine`).

```json
{ "statusLine": { "type": "command", "command": "\"C:/…/Daiso.StatusLine.exe\" --data \"C:/…/DAIso\"" } }
```

- 경로는 `/`로 쓰고 따옴표로 감싼다. Git Bash와 cmd가 둘 다 읽는다
- `--data`는 Host의 자료 폴더다. 없으면 `%LOCALAPPDATA%\DAIso`
- 고치기 전에 `settings.json.daiso-backup`으로 한 벌 복사한다. 원래 `statusLine`과 파일이 있었는지는 `claude-statusline-original.json`에 둔다. 끄면 그대로 돌린다

## 빌드

Host가 이 프로젝트를 참조하므로 Host 출력 폴더에 `Daiso.StatusLine.exe`가 같이 놓인다. Host는 자기 폴더의 exe를 등록한다.
뜨는 데 걸리는 시간: 처음 189ms, 그다음 63~70ms (2026-10-06, Debug 빌드). Claude가 응답마다 띄우므로 다른 프로젝트를 참조하지 않는다.
