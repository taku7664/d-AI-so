# 탭 플러그인 계약

VS Code처럼 **탭 하나 = 화면 모듈 하나 + 서버 엔드포인트 묶음 하나**로 만든다.
기본 탭 일곱 개(요약 · 터미널 · 세션 · 내 규칙 · 내 프롬프트 · 사용량 · 설정)부터 이 계약대로 만든다.

## 화면 쪽

`web/src/tabs/{id}/index.ts`에서 내보낸다.

```ts
export const tab: TabModule = {
  id: 'sessions', // 소문자. 서버 경로 /api/sessions/... 와 같다
  title: '세션',
  icon: 'list',
  order: 30,
  shortcut: 'Ctrl+3',
  View: SessionsView, // React 컴포넌트
};
```

## 서버 쪽

`src/Daiso.Server/Tabs/{Id}/`에 구현한다.

```csharp
public interface ITabEndpoints
{
    string Id { get; }                     // 화면 쪽 id 와 같다
    void Map(RouteGroupBuilder group);     // /api/{Id} 아래에 엔드포인트를 단다
}
```

## 알림

공용 WebSocket 하나(`/ws`)로 보낸다. 메시지는 이런 꼴이다.

```json
{ "tab": "sessions", "kind": "changed" }
```

웹은 받은 탭의 캐시를 무효화하고 다시 받는다 ([ARCHITECTURE.md](ARCHITECTURE.md) "화면과 서버가 주고받는 법").

## 범위

- **기본 탭이 실제로 쓰는 만큼만 계약에 넣는다.** 쓰이지 않는 확장 지점을 미리 만들지 않는다
- 바깥 플러그인이 탭을 더하는 길은 지금 만들지 않는다 ([DECISIONS.md](DECISIONS.md) "안 하기로 한 것")
- 지금 있는 **도구 플러그인**(`Providers.Manifest`, 어댑터 프로세스, `old/docs/PLUGIN_PLAN.md`)은 그대로 서버가 읽는다. 탭 플러그인과 다른 것이다
