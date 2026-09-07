# PROGRESS — 진행 상태 인수인계

컨텍스트가 압축되거나 세션이 바뀌어도 잃지 않아야 하는 것만 적는다.
마지막 갱신: 2026-09-08, 커밋 `8a92b5f`.

## 문서 구조 (역할 분담)

| 문서 | 역할 | 고칠 때 |
|---|---|---|
| `docs/REQUIREMENTS.md` | 무엇을 만드는가 | 요구가 바뀔 때만 |
| `docs/ARCHITECTURE.md` | 계층·모델·인터페이스 시그니처의 **정본**. 코드가 이를 따른다 | 코드보다 **먼저** 고치고, 커밋 메시지에 이유를 남긴다 |
| `docs/GOAL.md` | 목표·단계(Step)·완료 기준 | 단계 범위가 바뀔 때 |
| `README.md` | 빌드·실행·CLI·확인 기록 | 확인 결과가 바뀔 때 |
| `CLAUDE.md` | 커밋 규칙 (한글 한 줄, `타입: 내용`) | — |
| `samples/PROJECT_RULES.daiso` | .daiso 정본 샘플 | — |
| `docs/screenshots/` | README가 참조하는 화면 5장 | — |

진행 이력은 이 파일과 커밋 17개에 있다. 별도 로그 파일은 두지 않는다.

## 완료 상태

- **Stage 1 (Step 1–7)**: Core · Providers · Infrastructure · 검증용 CLI. 게이트 통과
- **Stage 2 (Step 8–16)**: WinUI 3 앱 5개 페이지 + 사용량 탭 + Context Doctor 탭. 게이트 통과
- 빌드 경고 0 / 오류 0, 테스트 212건 + Slow 2건 통과 (2026-09-08)
- 완성 상태 6항목 확인 결과는 README "수동 확인 체크리스트"에 있다

## ARCHITECTURE에서 벗어난 판단 3건 (모두 문서 반영됨)

1. **프로젝트 10개** — `ProjectPathNormalizer`가 `Path.GetFullPath`를 쓰므로 Core 순수성 규칙 아래 두 Provider가 공유할 수 없어 `Daiso.Providers.Common`을 추가
2. **`IUsageReader` / `IProjectFactsReader` 추가** — `IProvider`만으로는 Claude의 날짜별 사용량 합산과 REQ 5.3 부가 정보를 얻을 수 없어 별개 인터페이스로 분리 (기존 시그니처는 그대로)
3. **Codex `response_item.message` role=user → System** — 실제 세션에서 AGENTS 지시문·플러그인 목록이 이 경로로 주입되는 것을 확인. ARCH의 "시스템 주입 제외" 원칙에 맞춤

## 남은 일

| # | 항목 | 근거 | 상태 |
|---|---|---|---|
| 1 | `CLAUDE.md` ↔ `AGENTS.md` 마이그레이션 | REQUIREMENTS §7 표. Stage 1에서 제외했고 Stage 2 Step에도 없어 **미구현**. GOAL의 최종 수락 기준(REQ 1~7절 전부)에는 포함된다 | 미착수 |
| 2 | RuleMaker "프로젝트에 연동" **폴더 선택 대화상자** 확정 클릭 | GOAL Step 12 완료 기준 | 사람이 직접 확인 |
| 3 | `settings.json` 권한·훅 GUI 편집기 | REQ §7에서 **후순위(선택)** 로 표시 | 하지 않음 |

2번 참고: 같은 로직(`RuleFileService.EnsureInstruction`)은 CLI `rules install`(2회 실행 해시 동일)과 RuleMaker의 경로 직접 입력 버튼으로 이미 통과했다. 대화상자 자체가 앱 소유 창으로 열리는 것까지는 확인됐고, 마지막 확정 클릭만 남았다.

## 작업 방식 (지켜야 할 것)

- **지시받은 것만 한다.** 다음 단계로 넘어가기 전에 묻는다
- **앱을 띄우거나 UI를 자동 조작하지 않는다.** 화면 확인은 사용자가 직접 한다.
  코드·빌드·`dotnet test`·CLI까지가 내 범위다
- 사용자 폴더(`%LOCALAPPDATA%\d-AI-so\`)를 쓰거나 지우기 전에 묻는다
- 실제 세션 파일은 읽기 전용. 삭제 검증이 필요하면 더미 폴더를 세션 루트로 지정한다

## 참고: 앱이 쓰는 사용자 폴더

| 경로 | 내용 |
|---|---|
| `%LOCALAPPDATA%\d-AI-so\settings.json` | 최근 폴더·파일, 단가표, 정리 규칙, 테마, 창 크기 |
| `%LOCALAPPDATA%\d-AI-so\index.db` | 세션 인덱스. 실제 세션 248건에서 약 200MB. **2026-09-08 삭제함** (필요하면 앱이 다시 만든다) |
| `%LOCALAPPDATA%\d-AI-so\presets\*.daiso` | 프리셋 라이브러리 |
| `%LOCALAPPDATA%\d-AI-so\logs\crash-*.log` | 예외 로그 (토큰 마스킹 적용) |

인덱스 위치는 Settings 또는 `DAISO_INDEX_DB` 환경 변수로 바꿀 수 있다.
