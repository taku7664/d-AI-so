# 탭 (서버 쪽)

이 폴더의 하위 폴더 하나가 탭 하나의 서버 엔드포인트 묶음이다.
탭 하나 = **웹의 화면 모듈 하나 + 여기 있는 엔드포인트 묶음 하나**다. 화면 쪽 약속과 기본 탭 id 목록은 [frontend/web/src/tabs/README.md](../../../../frontend/web/src/tabs/README.md)에 있다.

## 탭을 만드는 법

`{Id}/` 폴더에 `ITabEndpoints`를 구현한다.

```csharp
public interface ITabEndpoints
{
    string Id { get; }                     // 화면 쪽 tab.id 와 같다
    void Map(RouteGroupBuilder group);     // /api/{Id} 아래에 엔드포인트를 단다
}
```

- 엔드포인트는 `/api/{Id}/` 아래에만 단다. 다른 탭의 경로를 쓰지 않는다
- 요청·응답 타입은 OpenAPI 문서에 나오게 만든다. 웹은 거기서 생성한 타입만 쓴다
- 보안 검사는 미들웨어가 한다. 탭에서 따로 넣거나 빼지 않는다 ([docs/SECURITY.md](../../../../docs/SECURITY.md))

## 알림을 보내는 법

데이터가 바뀌면 공용 WebSocket `/ws`로 이런 메시지를 보낸다.

```json
{ "tab": "sessions", "kind": "changed" }
```

무엇이 바뀌었는지만 알린다. 바뀐 데이터는 싣지 않는다. 화면이 다시 요청해서 받는다.

## 탭에 속하지 않는 경로

프로젝트 목록·계정·구독 한도처럼 여러 탭이 같이 쓰는 데이터는 `Tabs/`가 아니라 `Shared/`에 둔다. 목록은 [docs/ARCHITECTURE.md](../../../../docs/ARCHITECTURE.md) "탭에 속하지 않는 공용 경로"에 있다.

## 도구 플러그인과 다른 것

지금 있는 **도구 플러그인**(`Providers.Manifest`, 어댑터 프로세스, `old/docs/PLUGIN_PLAN.md`)은 AI CLI를 하나 더 붙이는 길이다. 탭과 상관없이 서버가 그대로 읽는다.
