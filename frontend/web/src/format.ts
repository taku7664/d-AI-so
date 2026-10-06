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

/** 812 B · 1.2 KB · 3.4 MB · 1.25 GB (1024 기준, 옛 화면과 같다) */
export function bytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 ** 2) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 ** 3) return `${(n / 1024 ** 2).toFixed(1)} MB`;
  return `${(n / 1024 ** 3).toFixed(2)} GB`;
}

/** 2026-10-06 14:20 (내 PC 시간). timeOnly 면 14:20 */
export function dateTime(iso: string, timeOnly = false): string {
  const at = new Date(iso);
  const pad = (v: number) => String(v).padStart(2, '0');
  const time = `${pad(at.getHours())}:${pad(at.getMinutes())}`;
  return timeOnly ? time : `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())} ${time}`;
}
