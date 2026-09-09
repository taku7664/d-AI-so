# d-AI-so 프로젝트 규칙

## 문서
- 요구사항: `docs/REQUIREMENTS.md`
- 아키텍처: `docs/ARCHITECTURE.md` (인터페이스·파싱 규칙의 정본. 코드가 이를 따른다)
- 목표·완료 기준: `docs/GOAL.md`
- 진행 중인 작업 계획: `docs/TERMINAL_CARD_PLAN.md` (새 터미널 카드 단계형 재설계, Stage 0~7)

## 빌드·실행 확인

앱을 띄워 확인할 때는 `tools/run-app.ps1`을 쓴다. 빌드한 바로 그 산출물을 실행한다.
`dotnet build`(솔루션)와 프로젝트 단독 빌드의 산출물 폴더는 같아야 하며(csproj의 기본 Platform x64), 손으로 적은 경로로 실행하지 않는다.
문구 키를 지우거나 바꾸면 `Daiso.Core.Tests`의 `StringResourceKeysTests`가 잡는다. 테스트가 빨간데 눈으로 넘기지 않는다.

## 커밋

커밋은 작업 단위 별로 필수로 커밋합니다.
**커밋한 뒤에는 바로 `git push origin main` 합니다.** 로컬에만 쌓아두지 않습니다.
특별한 상황이 아니면 main 브랜치에서 작업합니다.

이 저장소는 private입니다. 소스는 여기에만 둡니다.
공개 배포가 필요하면 별도의 public 저장소에 빌드 산출물(zip)만 올립니다.

**형식**

`타입: 내용` 또는 `타입(영역): 내용`

**타입**

| 타입 | 사용할 때 |
|---|---|
| `feat` | 새 기능, 새 메카닉 추가 |
| `fix` | 동작하지 않던 것 수정 |
| `refactor` | 동작은 같고 코드 구조만 변경 |
| `chore` | 설정, YAML 값 조정, 잡일 |
| `docs` | 주석, 재현 절차서, 문서 |

**작성 규칙**

- 한글로 씁니다.
- 한 줄로 씁니다. 마침표는 붙이지 않습니다.
- "수정", "변경"만 쓰지 말고 무엇을 어떻게 했는지 적습니다.
  - 나쁨: `fix(zone3): 버그 수정`
  - 좋음: `fix(zone3): 페이즈 전환이 매 타격마다 발동하던 문제 수정`

**커밋 전 확인**

- [ ] 다른 구역 파일을 건드리지 않았는가
- [ ] 커밋 메시지에 소스 로직이나 명령어가 노출되지 않았는가

**커밋 후**

- [ ] `git push origin main` 했는가

<!-- daiso:start -->
@PROJECT_RULES.daiso
Rules above are YAML. Priority MUST > SHOULD > MAY.
<!-- daiso:end -->
