# UI 토큰 정본화 계획

> 목표: **글자 크기 · 모서리 · 간격 눈금을 App.xaml 한 곳에서 정한다.**
> 지금은 이 값들이 XAML 여덟 파일에 숫자로 흩어져 있어, 한 번 바꾸려면 253곳을 찾아야 한다.
> 끝나면 App.xaml 의 값 한 줄을 고치는 것으로 화면 전체의 밀도·크기가 바뀌고,
> 새 숫자가 들어오면 테스트가 잡는다.

관련 문서: [ARCHITECTURE.md](ARCHITECTURE.md) §6.2 · [UI_REFACTOR_PLAN.md](UI_REFACTOR_PLAN.md) §6

---

## 1. 지금 상태 (2026-09-10 측정)

`{StaticResource}` 를 뺀 **날 숫자만** 센 것이다. `obj/` 의 빌드 생성 사본은 제외했다.

| 속성 | 리터럴 수 | 서로 다른 값 | 이번 범위 |
|---|---:|---:|---|
| `Spacing` | 172 | 12 | ✅ |
| `FontSize` | 72 | 9 | ✅ |
| `BorderThickness` | 13 | 4 | ✅ |
| `FontWeight` | 10 | 1 (`SemiBold`) | ❌ 숫자가 아니라 이름이다. 이미 뜻이 있다 |
| `CornerRadius` | 9 | 5 | ✅ |
| `Width` / `Height` | 276 | 54 | ❌ 자리마다 뜻이 다르다 (§5) |
| `Padding` / `Margin` | 59 | 41 | ❌ 자리마다 뜻이 다르다 (§5) |

**이번 범위 합계 266곳.**

파일별 (범위 안 속성만):

| 파일 | 곳 | 상태 |
|---|---:|---|
| TerminalPage.xaml | 미측정 | ⬜ |
| SessionsPage.xaml | 미측정 | ⬜ |
| RuleMakerPage.xaml | 미측정 | ⬜ |
| SettingsPage.xaml | 미측정 | ⬜ |
| DashboardPage.xaml | 미측정 | ⬜ |
| UsagePage.xaml | 미측정 | ⬜ |
| PromptsPage.xaml | 미측정 | ⬜ |
| ShellWindow.xaml | 미측정 | ⬜ |

> 착수 시 각 작업 단위에서 정확한 수를 채운다.

---

## 2. 규칙

1. **값의 정본은 `App.xaml` 하나다.** 다른 XAML 은 `{StaticResource}` 로만 참조한다.
2. **토큰은 값을 바꾸지 않는다.** 이 작업으로 화면 픽셀이 달라지면 안 된다.
   토큰 이름 → 숫자가 지금 그 자리의 숫자와 **똑같은지** 기계로 검증한다 (§4).
3. **App.xaml 에 새 값을 만들지 않는다.** 실제로 쓰이는 값에만 이름을 붙인다.
   쓰이지 않는 "혹시 몰라서" 토큰은 두지 않는다.
4. 토큰 이름에 `_` 를 쓰지 않는다. `StringResourceKeysTests` 가 `접두어_이름` 꼴 문자열을
   문구 키로 보기 때문이다.
5. **눈금에서 벗어난 값은 이름으로 드러낸다.** 숨기지 않는다 (§3 참고).

---

## 3. 토큰

### 3.1 글자 크기 — `x:Double`

실제로 쓰이는 열 값에 역할 이름을 붙인다. 값은 그대로다.
(아홉은 인라인, 24 는 `App.xaml` 의 `PageTitleText` 스타일 안에 있었다.)

| 토큰 | 값 | 쓰이는 곳 |
|---|---:|---:|
| `CaptionSmallFontSize` | 11 | 2 |
| `CaptionFontSize` | 12 | 26 |
| `BodySmallFontSize` | 13 | 16 |
| `BodyFontSize` | 14 | 20 |
| `SubtitleFontSize` | 16 | 2 |
| `TitleSmallFontSize` | 18 | 1 |
| `TitleFontSize` | 20 | 1 |
| `HeadingFontSize` | 22 | 3 |
| `HeadingLargeFontSize` | 24 | 1 (`PageTitleText` 스타일) |
| `DisplayFontSize` | 28 | 1 |

> **보고 사항:** 18 · 20 · 28 은 각각 한 곳에서만 쓰인다. 아홉 단계는 눈금이라기보다 목록이다.
> 다섯 단계로 줄이면 화면 글자 크기가 실제로 달라지므로 **디자인 결정**이고, 이 작업의 범위가 아니다.
> 토큰으로 모아 두면 그 판단을 나중에 한 곳에서 할 수 있다 — 그게 이 작업의 목적이다.

### 3.2 모서리 — `CornerRadius`

| 토큰 | 값 |
|---|---:|
| `RadiusSmall` | 4 |
| `RadiusMedium` | 6 |
| `RadiusLarge` | 8 |
| `RadiusPill` | 10 |
| `RadiusRound` | 14 |

`App.xaml` 의 `CardBorder`(8) · `NoticeBorder`(6) · `PillBorder`(10) 스타일도 이 토큰을 쓰게 바꾼다.

### 3.3 간격 — `x:Double`

`App.xaml` 은 "간격은 4의 배수"라고 적어 두었다. 실제로는 **아니다**:

| 토큰 | 값 | 쓰이는 곳 | 4의 배수 |
|---|---:|---:|---|
| `GapHair` | 1 | 4 | ✗ |
| `GapTiny` | 2 | 14 | ✗ |
| `GapMicro` | 3 | 3 | ✗ |
| `GapXSmall` | 4 | 7 | ✓ |
| `GapSmall` | 6 | 10 | ✗ |
| `GapMedium` | 8 | 55 | ✓ |
| `GapOffGrid10` | 10 | 27 | ✗ |
| `GapLarge` | 12 | 37 | ✓ |
| `GapOffGrid14` | 14 | 6 | ✗ |
| `GapXLarge` | 16 | 7 | ✓ |
| `GapHuge` | 28 | 1 | ✓ |

> **보고 사항:** 10 과 14 는 33곳에서 쓰이면서 문서화된 4배수 규칙을 어긴다.
> 8/12/16 으로 스냅하면 화면 밀도가 바뀌므로 **디자인 결정**이고 하지 않는다.
> 대신 이름(`GapOffGrid10`, `GapOffGrid14`)으로 드러내 두어, 나중에 정리할 때 어디를 봐야 하는지 남긴다.
> 1 · 2 · 3 도 눈금 밖이지만 대부분 아이콘·배지 안쪽 간격이라 성격이 다르다.

### 3.4 테두리 — `Thickness`

`BorderThickness` 네 값은 자리마다 뜻이 달라(`0,1,0,0` 같은 한쪽 테두리) 이름을 붙이기 어렵다.
**균일한 값만** 토큰으로 만들고 나머지는 둔다.

---

## 4. 검증

값을 바꾸지 않았음을 눈이 아니라 기계로 확인한다.

1. **치환 검증 스크립트** — 각 작업 단위마다 돌린다.
   바꾸기 전 XAML 과 바꾼 뒤 XAML 에서, 모든 `속성="값"` 을 토큰을 풀어 숫자로 되돌린 뒤 비교한다.
   하나라도 다르면 그 작업 단위는 틀린 것이다.
2. **빌드** — 앱이 실행 중이면 산출물 DLL 이 잠기므로 `-p:OutputPath=<임시>` 로 빌드한다.
   경고 0 · 오류 0 이어야 한다 (`TreatWarningsAsErrors`).
3. **가드 테스트** — 마지막 작업 단위에서 켠다.
   `Daiso.Core.Tests` 가 App 소스를 훑어 범위 안 속성에 날 숫자가 남아 있으면 실패한다.
   (`StringResourceKeysTests` 와 같은 방식이다. `App.xaml` 은 정본이므로 예외.)

**앱은 실행하지 않는다.** 픽셀 동일성은 §4.1 의 치환 검증이 보장한다.

---

## 5. 이번에 하지 않는 것

| 항목 | 이유 |
|---|---|
| `Width` / `Height` 276곳 | 아이콘 크기 · 열 너비 · 막대 길이가 섞여 있다. 하나로 묶으면 거짓말이 된다 |
| `Padding` / `Margin` 59곳 | 41종이 거의 다 그 자리 전용이다. 토큰화 이득이 없다 |
| 글자 크기 9단계 → 5단계 축소 | 화면이 실제로 달라진다. 디자인 결정 |
| 간격 10 · 14 를 눈금에 스냅 | 위와 같다 |
| `Card` · `StatTile` 같은 컴포넌트 어휘 | 별건 (트리 깊이 16 문제). 이 작업과 독립이다 |

---

## 6. 작업 단위와 진행

작은 파일부터 간다. 첫 파일에서 방식이 틀렸으면 싸게 되돌린다.
각 단위는 **커밋 하나 + 이 문서 갱신**이다.

| # | 작업 단위 | 상태 |
|---|---|---|
| W0 | 이 문서 · `App.xaml` 토큰 정의 · 치환 검증 스크립트 | ⬜ |
| W1 | ShellWindow.xaml | ⬜ |
| W2 | PromptsPage.xaml | ⬜ |
| W3 | UsagePage.xaml | ⬜ |
| W4 | DashboardPage.xaml | ⬜ |
| W5 | SettingsPage.xaml | ⬜ |
| W6 | RuleMakerPage.xaml | ⬜ |
| W7 | SessionsPage.xaml | ⬜ |
| W8 | TerminalPage.xaml | ⬜ |
| W9 | 가드 테스트 · App.xaml 기존 스타일 정리 · 마무리 | ⬜ |

## 7. 작업 기록

(작업 단위가 끝날 때마다 여기에 한 줄씩 남긴다)
