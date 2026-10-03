# 탭 (화면 쪽)

이 폴더의 하위 폴더 하나가 탭 하나다. 왼쪽 메뉴에 뜨는 탭은 전부 여기서 온다.
탭 하나 = **여기 있는 화면 모듈 하나 + 서버의 엔드포인트 묶음 하나**다. 서버 쪽 약속은 [backend/src/Daiso.Host/Tabs/README.md](../../../../backend/src/Daiso.Host/Tabs/README.md)에 있다.

## 탭을 만드는 법

`{id}/index.ts`에서 `tab`을 내보낸다.

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

| 필드 | 규칙 |
|---|---|
| `id` | 소문자와 `-`만. 서버 쪽 `ITabEndpoints.Id`와 같아야 한다. 한 번 정하면 바꾸지 않는다 |
| `order` | 메뉴 순서. 10 단위로 띄워 사이에 끼울 자리를 남긴다 |
| `shortcut` | `Ctrl+1`~`Ctrl+7`은 기본 탭이 쓴다 |
| `View` | 서버 데이터는 생성된 API 클라이언트로만 받는다. 직접 `fetch`하지 않는다 |

## 서버 알림을 받는 법

서버는 공용 WebSocket `/ws`로 `{ "tab": "{id}", "kind": "changed" }`를 보낸다.
탭은 자기 `id`로 온 알림을 받으면 그 탭의 캐시를 무효화하고 다시 받는다. 알림 안의 값을 믿고 화면을 고치지 않는다.

## 기본 탭

| id | 제목 | order |
|---|---|---|
| `dashboard` | 요약 | 10 |
| `terminal` | 터미널 | 20 |
| `sessions` | 세션 | 30 |
| `rules` | 내 규칙 | 40 |
| `prompts` | 내 프롬프트 | 50 |
| `usage` | 사용량 | 60 |
| `settings` | 설정 | 70 |

## 범위

- **기본 탭이 실제로 쓰는 만큼만 약속에 넣는다.** 쓰이지 않는 확장 지점을 미리 만들지 않는다
- 바깥 플러그인이 탭을 더하는 길은 지금 만들지 않는다 ([docs/DECISIONS.md](../../../../docs/DECISIONS.md) "안 하기로 한 것")
