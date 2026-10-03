# 문서 안내

WinUI 3 앱을 **Electron + 웹 화면 + C# 서버**로 옮기는 작업의 문서다. 무엇을 어디서 찾는지, 어떻게 일하는지, 지금 어디까지 왔는지를 적는다.
2026-10-03까지의 앱과 문서는 전부 `old/`에 있다.

## 지금 어디까지 됐나

| 날짜 | 한 일 |
|---|---|
| 2026-10-03 | 작업물을 `old/`로 옮김. 문서를 주제별로 나눔. 루트에 ESLint·Prettier·EditorConfig 설정을 둠 |

다음은 [ROADMAP.md](ROADMAP.md)의 Stage 0이다.

## 문서 목록

| 문서 | 무엇을 정하나 | 언제 읽나 |
|---|---|---|
| [ROADMAP.md](ROADMAP.md) | Stage 0~8, 단계마다 완료 기준, 위험과 되돌리기 | 일을 시작할 때 |
| [DECISIONS.md](DECISIONS.md) | 왜 바꾸나, 정한 것과 버린 안, 안 하기로 한 것, 미정 | 방향을 바꾸고 싶을 때 |
| [ARCHITECTURE.md](ARCHITECTURE.md) | 프로세스 구성, 저장소 구조, 상태의 주인, 창과 크롬 탭의 차이 | 코드를 어디에 둘지 정할 때 |
| [SECURITY.md](SECURITY.md) | localhost 서버 보안 규칙 일곱 가지 | 서버 엔드포인트를 만들 때 |
| [TAB_PLUGINS.md](TAB_PLUGINS.md) | 탭 하나를 이루는 화면 모듈과 서버 엔드포인트의 계약 | 탭을 만들 때 |
| [MIGRATION_MAP.md](MIGRATION_MAP.md) | 옛 `Daiso.App` 코드가 서버·Electron·웹 중 어디로 가는지 | 옛 화면을 옮길 때 |

옛 문서 중 아직 정본인 것:

| 문서 | 무엇 |
|---|---|
| `old/docs/ARCHITECTURE.md` | Core 모델, 파싱 규칙, Core 순수성 규칙. 백엔드는 여전히 이걸 따른다 |
| `old/docs/RELEASE.md` | 배포 원칙. Stage 8에서 `docs/`로 옮겨 고친다 |
| `old/docs/PLUGIN_PLAN.md` | 도구 플러그인(매니페스트·어댑터). 탭 플러그인과는 다른 것이다 |

화면 시안: https://claude.ai/artifact/DDRz5Lv416wn35na37WCPS (요약 · 터미널 · 세션)

## 일하는 방식

- **Stage 순서대로 한다.** 앞 단계의 완료 기준을 채우기 전에 다음을 시작하지 않는다
- **단계마다 커밋하고 `git push origin main`.** 이 표의 "지금 어디까지 됐나"도 같이 고친다
- **`old/`는 Stage 8 전까지 지우지 않는다.** 새 앱이 다 따라잡을 때까지 옛 앱이 빌드·배포 가능해야 한다
- **`old/src/Daiso.App`은 얼린다.** 급한 배포가 아니면 고치지 않는다. 필요한 로직은 읽고 새 쪽에 다시 쓴다
- **백엔드(`old/src/Daiso.Core` · `Providers.*` · `Infrastructure`)는 옛 앱과 새 서버가 같이 쓴다.** 고치면 `old/` 솔루션의 빌드·테스트도 초록이어야 한다
- **[DECISIONS.md](DECISIONS.md)에 적힌 결정은 다시 논의하지 않는다.** 바꾸려면 그 문서에 이유를 적고 바꾼다
- 코딩 규칙은 루트 `PROJECT_RULES.daiso`가 정본이다. ESLint(`eslint.config.mjs`)는 그중 기계가 잡을 수 있는 것만 건다

## 이 문서를 덮고 할 일

[ROADMAP.md](ROADMAP.md)를 열고 Stage 0부터 한다.
