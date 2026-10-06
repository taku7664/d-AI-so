// 숫자·날짜를 화면 글로. 정확한 값은 툴팁에 따로 넣는다

/** 1.9B · 1.2M · 340K · 812 */
export function tokens(n: number): string {
  if (n >= 1_000_000_000) return `${(n / 1_000_000_000).toFixed(n >= 10_000_000_000 ? 0 : 1)}B`;
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(n >= 10_000_000 ? 0 : 1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(n >= 10_000 ? 0 : 1)}K`;
  return String(n);
}

/** 1,234,567 */
export function exact(n: number): string {
  return n.toLocaleString('ko-KR');
}

/** 0.612 → 61% */
export function percent(share: number): string {
  return `${Math.round(share * 100)}%`;
}
