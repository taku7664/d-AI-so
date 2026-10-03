# 신규 기본 프롬프트 5종 추가 계획 (2026-09-10)

## 1. 기능 한 줄 요약과 이유

실무 개발 워크플로우에 유용한 절차형 기본 프롬프트 5종(`feature-proposal`, `api-spec`, `code-review`, `test-suite`, `performance-tune`)을 `Daiso.Core` 임베디드 리소스와 카탈로그에 추가한다.
이유: 기존 기본 프롬프트(7종)만으로는 코드 검토, 기능 제안/비교, API 설계, 테스트 보강, 성능 튜닝 등 일상적인 개발 작업 범위를 충분히 커버하기 부족하다고 판단(사용자 요청).

- 상태: `[CONFIRMED]`
- 사용자: 사용자 본인 및 외부 사용자

## 2. 완료 조건

1. 신규 프롬프트 파일 5종이 `src/Daiso.Core/Resources/Prompts/`에 추가된다:
   - `feature-proposal.md` (`category: planning`, `output: docs/PROPOSAL.md`)
   - `api-spec.md` (`category: planning`, `output: docs/API_SPEC.md`)
   - `code-review.md` (`category: understanding`, `output: docs/CODE_REVIEW.md`)
   - `test-suite.md` (`category: fixing`, `output: docs/TEST_PLAN.md`)
   - `performance-tune.md` (`category: fixing`, `output: docs/PERFORMANCE.md`)
2. 모든 신규 프롬프트가 `PromptPresetTests` 규격을 준수한다:
   - 정확한 Front matter 필드(`name`, `description`, `category`, `output`) 보유.
   - 단일 `# `(H1) 제목 사용.
   - 코딩 에이전트의 선제적 파일 생성을 방지하는 문구("파일을 만들거나", "확인받기 전에는") 포함.
   - 1단계(또는 1회차) 5개 번호 질문 인터뷰 형식 준수.
3. `src/Daiso.Core/Prompts/PromptPreset.cs`의 `BuiltInPrompts.Catalog`에 5종이 논리적 순서에 맞춰 등록된다.
4. `dotnet test tests/Daiso.Core.Tests`를 실행했을 때 모든 테스트가 경고 없이 통과한다 (`Built_in_catalog_and_embedded_files_match`, `Every_built_in_prompt_parses_and_says_where_it_writes`, `Built_in_prompts_use_a_single_h1`).
5. (선택) 로컬 프로젝트 참조용 `docs/prompts/`에도 동일하게 반영된다.

## 3. 영향 범위

- **모듈·파일**:
  - `src/Daiso.Core/Resources/Prompts/feature-proposal.md` (신규)
  - `src/Daiso.Core/Resources/Prompts/api-spec.md` (신규)
  - `src/Daiso.Core/Resources/Prompts/code-review.md` (신규)
  - `src/Daiso.Core/Resources/Prompts/test-suite.md` (신규)
  - `src/Daiso.Core/Resources/Prompts/performance-tune.md` (신규)
  - `src/Daiso.Core/Prompts/PromptPreset.cs` (카탈로그 배열 수정)
  - `docs/prompts/*.md` (신규 5종 동기화)
- **재사용**:
  - `PromptPresetSerializer` 파서 및 유효성 검사 로직
  - 기존 7종 기본 프롬프트 구조(5개 번호 질문, 단계적 확인 절차, 출력 파일 지정)
- **같이 바뀌어야 하는 것**:
  - `BuiltInPrompts.Catalog`
  - `Daiso.Core.Tests` 테스트 검증
- **깨질 수 있는 것**:
  - 프런트매터 문법 오류나 허용되지 않은 카테고리값 기재 시 파싱 예외 발생 가능
  - H1 태그 다중 사용 시 기존 테스트 실패
- **아직 모르는 것**:
  - 없음 (요구사항 확정)

## 4. 범위

- **첫 번째 판 (이번 작업)**:
  - 5종 프롬프트 파일 작성 및 `src/Daiso.Core/Resources/Prompts/` 임베드
  - `BuiltInPrompts.Catalog` 업데이트
  - `docs/prompts/` 동기화
  - `Daiso.Core.Tests` 실행 및 통과 확인
- **뒤로 미루는 것**:
  - 프롬프트 목록 뷰(PromptsPage) 카테고리 UI 레이아웃 개편
- **하지 않는 것**:
  - 기존 7종 프롬프트 수정
  - 파서 구조 변경

## 5. 순서와 각 단계의 완료 조건

1. **1단계 — 프롬프트 파일 5종 작성 (`src/Daiso.Core/Resources/Prompts/`)**
   - 완료 조건: 5개 마크다운 파일이 프런트매터(name, description, category, output), 단일 H1, 5개 인터뷰 질문, 파일 생성 방지 문구를 갖추어 UTF-8 LF로 생성됨.
2. **2단계 — 카탈로그 등록 (`src/Daiso.Core/Prompts/PromptPreset.cs`)**
   - 완료 조건: `Catalog` 배열에 5종 ID가 기획(Planning) → 파악(Understanding) → 수정(Fixing) 카테고리 흐름에 맞추어 추가됨.
3. **3단계 — `docs/prompts/` 동기화**
   - 완료 조건: 프로젝트 로컬 `docs/prompts/`에도 프롬프트 5종이 복사/생성됨.
4. **4단계 — 테스트 및 빌드 검증**
   - 완료 조건: `dotnet test`로 `PromptPresetTests`를 실행하여 100% 통과 확인.

## 6. 지키는 제약

- 모든 파일은 UTF-8 인코딩 및 LF 줄바꿈을 사용한다.
- 기존 코딩 규칙(`PROJECT_RULES.daiso`)을 엄격히 준수한다.
- 기존 기본 프롬프트(7종)는 수정하지 않는다.

## 7. 결정 기록과 미정 사항

- **`feature-proposal`**: 기능 제안/비교 (`planning`, `docs/PROPOSAL.md`) `[CONFIRMED]`
- **`api-spec`**: API 및 모델 설계 (`planning`, `docs/API_SPEC.md`) `[CONFIRMED]`
- **`code-review`**: 코드 검토 (`understanding`, `docs/CODE_REVIEW.md`) `[CONFIRMED]`
- **`test-suite`**: 테스트 케이스 보강 (`fixing`, `docs/TEST_PLAN.md`) `[CONFIRMED]`
- **`performance-tune`**: 성능 병목 분석 (`fixing`, `docs/PERFORMANCE.md`) `[CONFIRMED]`

## 8. 첫 작업

`src/Daiso.Core/Resources/Prompts/`에 5종의 프롬프트 마크다운 파일 작성.
