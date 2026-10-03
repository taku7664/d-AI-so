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
| `Spacing` · `RowSpacing` · `ColumnSpacing` · `MinRowSpacing` · `MinColumnSpacing` | 172 | 12 | ✅ |
| `FontSize` | 72 | 9 | ✅ |
| `BorderThickness` | 13 | 4 | ✅ |
| `FontWeight` | 10 | 1 (`SemiBold`) | ❌ 숫자가 아니라 이름이다. 이미 뜻이 있다 |
| `CornerRadius` | 9 | 5 | ✅ |
| `Width` / `Height` | 276 | 54 | ❌ 자리마다 뜻이 다르다 (§5) |
| `Padding` / `Margin` | 59 | 41 | ❌ 자리마다 뜻이 다르다 (§5) |

**이번 범위 합계 266곳.**

파일별 (범위 안 속성만):

| 파일 | 범위 안 곳 | 상태 |
|---|---:|---|
| TerminalPage.xaml | 83 | ✅ W8 |
| SessionsPage.xaml | 43 | ✅ W7 |
| RuleMakerPage.xaml | 42 | ✅ W6 |
| DashboardPage.xaml | 31 | ✅ W4 |
| SettingsPage.xaml | 26 | ✅ W5 |
| UsagePage.xaml | 18 | ✅ W3 |
| PromptsPage.xaml | 11 | ✅ W2 |
| ShellWindow.xaml | 5 | ✅ W1 |
| App.xaml (스타일 안) | 9 | ✅ W0 |

> 합계 **268곳**. `{StaticResource}` 를 뺀 날 숫자만 센 것이고, `obj/` 의 빌드 생성 사본은 제외했다.

---

## 2. 규칙

1. **값의 정본은 `App.xaml` 하나다.** 다른 XAML 은 `{StaticResource}` 로만 참조한다.
2. **토큰은 값을 바꾸지 않는다.** 이 작업으로 화면 픽셀이 달라지면 안 된다.
   토큰 이름 → 숫자가 지금 그 자리의 숫자와 **똑같은지** 기계로 검증한다 (§4).
3. **App.xaml 에 새 값을 만들지 않는다.** 실제로 쓰이는 값에만 이름을 붙인다.
   쓰이지 않는 "혹시 몰라서" 토큰은 두지 않는다.
5. 토큰 이름에 `_` 를 쓰지 않는다. `StringResourceKeysTests` 가 `접두어_이름` 꼴 문자열을
   문구 키로 보기 때문이다.
6. **눈금에서 벗어난 값은 이름으로 드러낸다.** 숨기지 않는다 (§3 참고).

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
3. **가드 테스트** — `tests/Daiso.Core.Tests/Architecture/UiTokenTests.cs` (W9 에서 켬).
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
| W0 | 이 문서 · `App.xaml` 토큰 정의 · 치환 검증 스크립트 | ✅ |
| W1 | ShellWindow.xaml | ✅ |
| W2 | PromptsPage.xaml | ✅ |
| W3 | UsagePage.xaml | ✅ |
| W4 | DashboardPage.xaml | ✅ |
| W5 | SettingsPage.xaml | ✅ |
| W6 | RuleMakerPage.xaml | ✅ |
| W7 | SessionsPage.xaml | ✅ |
| W8 | TerminalPage.xaml | ✅ |
| W9 | 가드 테스트 · App.xaml 기존 스타일 정리 · 마무리 | ✅ |

## 7. 작업 기록

(작업 단위가 끝날 때마다 여기에 한 줄씩 남긴다)

- **W0** — `App.xaml` 에 토큰 34개 정의(글자 10 · 모서리 5 · 간격 11 + 기존 값 8). 기존 스타일 9곳이 토큰을 쓰게 바꿈.
  치환 검증 스크립트(§4.1)와 기계 치환 스크립트를 만들었다. 손으로 고치지 않는다 — 오타로 값이 바뀔 여지를 없앤다.
- **W1~W3** — ShellWindow(5) · PromptsPage(11) · UsagePage(18). 값 34곳 전부 동일, 남은 날 숫자 0. 빌드 경고 0.
  - *도중에 잡은 것:* 첫 조사가 `ColumnSpacing` · `RowSpacing` 을 `Spacing` 의 부분 문자열로 세고 있었다.
    수는 우연히 맞았지만(172) 치환 도구는 이들을 건너뛰고 있었다. 두 도구 모두 복합 이름을 알도록 고쳤다.
  - *도중에 잡은 것:* `PageTitleText` 스타일의 `FontSize 24` 가 첫 목록에 없었다(인라인 속성만 셌기 때문).
    `HeadingLargeFontSize` 를 더해 열 단계가 되었다.
- **W4~W5** — DashboardPage(31) · SettingsPage(26). 값 57곳 전부 동일, 남은 날 숫자 0. 빌드 경고 0.
- **W6~W7** — RuleMakerPage(42) · SessionsPage(43). 값 85곳 전부 동일. 빌드 경고 0.
  SessionsPage 의 `Spacing="0"` 한 곳은 토큰을 만들지 않고 두었다 (§2 규칙 3).
- **W8** — TerminalPage(83). 값 전부 동일. 빌드 경고 0.
  - *도중에 잡은 것:* 치환 도구가 `<Setter Property="CornerRadius" Value="4" />` 형태를 몰라 6곳을 건너뛰고 있었다.
    **검증 스크립트가 이 누락을 잡았다** — 검증기는 처음부터 `<Setter>` 를 같은 자리로 세고 있었기 때문이다.
    도구가 서로를 감시하게 둔 것이 값어치를 했다. 다른 화면에는 `<Setter>` 형태가 없다(확인함).
  - 이 때문에 TerminalPage 의 실제 곳 수는 77 이 아니라 83 이고, 합계는 268 이다.
- **W9** — 가드 테스트 `UiTokenTests` 를 켰다. 세 가지를 잠근다:
  1. `App.xaml` 밖에서 범위 안 속성에 날 숫자를 쓰면 실패 (`0` 만 예외)
  2. 화면이 부르는 토큰이 `App.xaml` 에 없으면 실패 — 없는 자원 참조는 **빌드가 아니라 실행 중에** 터진다
  3. `App.xaml` 에 아무도 안 쓰는 토큰이 있으면 실패
  검사기 자체가 도는지 확인하는 예제 9개(어겨야 빨개지는 것 5 · 통과해야 하는 것 4)를 함께 둔다.
  - *도중에 잡은 것:* 3번이 처음에 `PageMaxWidth` 를 죽었다고 잡았다. XAML 만 훑었기 때문인데,
    실제로는 `PageBody.cs` 가 `Resources["PageMaxWidth"]` 로 코드에서 꺼내 쓴다. C# 참조도 세도록 고쳤다.
  - *도중에 잡은 것:* `TitleBarCompactHeight` 등 세 개는 **WinUI 가 스스로 읽어 가는** 기본값 덮어쓰기라
    우리 코드 어디서도 이름을 부르지 않는다. 지우면 제목줄이 되돌아가므로 예외 목록에 이유와 함께 적었다.

---

## 8. 결과

| | 전 | 후 |
|---|---:|---:|
| 화면 XAML 의 날 숫자 (범위 안) | 268 | **1** (`Spacing="0"`) |
| 값을 바꾸려면 봐야 할 곳 | 9개 파일 | **`App.xaml` 1개** |
| 되돌아감을 막는 장치 | 없음 | `UiTokenTests` 3종 |

글자를 한 단계 키우거나 화면 밀도를 줄이는 일이 이제 `App.xaml` 의 숫자 한 줄이다.
