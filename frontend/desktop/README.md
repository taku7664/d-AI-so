# desktop (Electron 메인 + preload)

`Daiso.Host`를 자식 프로세스로 띄우고, Host가 알려 준 주소를 창에 연다. 창을 닫으면 트레이에 남는다.
화면 자체는 Host가 내준다. 이 폴더에는 창·트레이·Host 띄우기만 있다.

## 띄우기

저장소 루트에서 실행한다. Host와 desktop을 빌드하고, 빌드한 바로 그 Host 경로를 `DAISO_HOST_EXE`로 넘겨 Electron을 띄운다.

```powershell
tools/run-app.ps1
```

`-NoBuild`를 주면 빌드를 건너뛴다. 이미 떠 있는 이 저장소의 Electron은 먼저 끈다.

- Electron 44는 `npm install` 때 실행 파일을 받지 않는다. 스크립트가 `require('electron')`을 부를 때 없으면 받아 온다
- `DAISO_HOST_EXE` 없이 `electron desktop`으로 띄우면 시작하지 못했다는 창이 뜬다. Host 경로를 짐작하지 않는다
- 설치판(Stage 8)에서는 `resources/host/Daiso.Host.exe`를 쓴다

## Host와 주고받는 것

| 무엇 | 어떻게 |
|---|---|
| 토큰 | 실행할 때마다 32바이트 난수(base64url)를 만들어 `DAISO_TOKEN`으로 넘긴다. 로그에 남기지 않는다 |
| 부모 PID | `DAISO_PARENT_PID`. Host가 이 프로세스가 끝나는 것을 보고 스스로 끝난다 |
| 주소 | Host 표준 출력의 `DAISO_LISTENING http://127.0.0.1:{port}` 줄을 30초까지 기다린다. 그 전에 Host가 끝나면 오류 창을 띄우고 앱을 닫는다 |

자세한 약속은 [backend/src/Daiso.Host/README.md](../../backend/src/Daiso.Host/README.md)와 [docs/SECURITY.md](../../docs/SECURITY.md)에 있다.

## 끌 때 Host가 남지 않는 이유

Host는 `detached: true`로 띄운다. Windows에서 Node(libuv)는 자식을 "부모가 죽으면 같이 죽이는" 잡 오브젝트에 넣는다. 그대로 두면 앱을 끌 때 Host가 정리할 틈 없이 죽어 `server.json`이 남는다(2026-10-06 재현). 잡에서 빼면 Host의 부모 감시(`ParentWatcher`)가 정상 종료를 맡는다.

| 끄는 길 | 확인 |
|---|---|
| Electron 메인을 강제로 끔 | Host가 끝나고 `server.json`이 지워진다 (2026-10-06) |
| 트레이 "끝내기" | 같은 부모 감시를 탄다. 화면을 눌러 보지는 않았다 |

Host가 띄우는 터미널(Stage 6)도 이제 Electron의 잡 밖에 있다. Host가 끝날 때 자기 자식을 정리해야 한다.

## 창

- `contextIsolation`·`sandbox` 켬, `nodeIntegration` 끔
- 창은 Host 출처 안에서만 움직인다. 바깥 http·https 링크는 기본 브라우저로 넘기고, 그 밖의 주소는 열지 않는다
- X는 트레이로 숨긴다. 트레이 메뉴: 열기 · 브라우저로 열기 · 끝내기. 트레이 아이콘을 누르면 창을 꺼낸다
- 두 번째로 실행하면 새 앱은 바로 끝나고, 떠 있는 앱의 창이 앞으로 나온다

## preload

`window.daisoDesktop`을 연다 ([docs/DECISIONS.md](../../docs/DECISIONS.md) "Electron 전용 기능은 좁은 다리 하나로"). 지금은 빈 객체다. 화면은 이 객체가 있는지만 보고 Electron 전용 기능을 켠다. 쓰는 기능이 생길 때 하나씩 더한다.
