# 탭 (화면 쪽)

이 폴더의 하위 폴더 하나가 탭 하나다. 왼쪽 메뉴에 뜨는 탭은 전부 여기서 온다.
탭 하나 = **여기 있는 화면 모듈 하나 + 서버의 엔드포인트 묶음 하나**다. 서버 쪽 약속은 [backend/src/Daiso.Host/Tabs/README.md](../../../../backend/src/Daiso.Host/Tabs/README.md)에 있다.

## 탭을 만드는 법

`{id}/index.tsx`에서 `tab`을 내보내고 [registry.ts](registry.ts)에 한 줄 더한다. 타입은 [types.ts](types.ts).

```tsx
export const tab: TabModule = {
  id: 'sessions', // 소문자. 서버 경로 /api/sessions/... 와 같다
  title: t('tab.sessions'), // 문구는 strings/ko.json 에
  icon: 'chat-left-text', // Bootstrap Icons 이름
  order: 30,
  shortcut: 'Ctrl+3',
  inMenu: true,
  View: SessionsView, // React 컴포넌트
};
```

탭 주소는 `/{id}`다(요약만 `/`). 크롬 탭에서 그 주소로 바로 열 수 있다.

| 필드 | 규칙 |
|---|---|
| `id` | 소문자와 `-`만. 서버 쪽 `ITabEndpoints.Id`와 같아야 한다. 한 번 정하면 바꾸지 않는다 |
| `order` | 메뉴 순서. 10 단위로 띄워 사이에 끼울 자리를 남긴다 |
| `shortcut` | `Ctrl+1`~`Ctrl+6`은 기본 탭이 쓴다. `Ctrl+P`는 프로젝트 바꾸기다 |
| `inMenu` | 왼쪽 메뉴에 띄울지. 기본 `true`. 설정만 `false`이고 위 줄 톱니로 연다 |
| `View` | 서버 데이터는 생성된 API 클라이언트(`src/api/client.ts`)로만 받는다. 직접 `fetch`하지 않는다. 쿼리 키 첫 칸은 탭 id다 |

## 서버 알림을 받는 법

서버는 공용 WebSocket `/ws`로 `{ "tab": "{id}", "kind": "changed" }`를 보낸다.
탭은 자기 `id`로 온 알림을 받으면 그 탭의 캐시를 무효화하고 다시 받는다. 알림 안의 값을 믿고 화면을 고치지 않는다.

## 기본 탭

2026-10-06에 확정했다 ([docs/DECISIONS.md](../../../../docs/DECISIONS.md) "탭은 여섯 개"). id는 바꾸지 않는다. 화면에 보이는 제목만 바꿀 수 있다.

| id | 제목 | order | 메뉴 |
|---|---|---|---|
| `dashboard` | 요약 | 10 | 예 |
| `terminal` | 터미널 | 20 | 예 |
| `sessions` | 세션 | 30 | 예 |
| `rules` | 지침 | 40 | 예 |
| `prompts` | 프롬프트 | 50 | 예 |
| `usage` | 사용량 | 60 | 예 |
| `settings` | 설정 | 70 | 아니오 (톱니) |

## 지금 프로젝트

위 줄에서 고른 프로젝트가 모든 탭의 기준이다. 탭은 프로젝트를 직접 고르지 않고 공용 상태에서 받는다. "모든 프로젝트"(프로젝트 없음)일 때 무엇을 보여 줄지는 탭이 정한다. 예를 들어 지침은 프로젝트마다 따로 있으므로 프로젝트를 먼저 고르게 한다.

## 범위

- **기본 탭이 실제로 쓰는 만큼만 약속에 넣는다.** 쓰이지 않는 확장 지점을 미리 만들지 않는다
- 바깥 플러그인이 탭을 더하는 길은 지금 만들지 않는다 ([docs/DECISIONS.md](../../../../docs/DECISIONS.md) "안 하기로 한 것")
