// 화면 문구. 문구는 ko.json 한 곳에만 둔다. 키가 없으면 타입 검사가, 안 쓰는 키는 strings.test.ts 가 잡는다
import ko from './ko.json';

export type StringKey = keyof typeof ko;

/** 문구를 꺼낸다. `{name}` 자리는 values 로 채운다. */
export function t(key: StringKey, values?: Record<string, string | number>): string {
  const text: string = ko[key];
  if (!values) return text;
  return text.replace(/\{(\w+)\}/g, (whole, name: string) => (name in values ? String(values[name]) : whole));
}

/** 키가 있는가. 서버가 문구 키를 보낼 때(로그인 부가 정보 등) 모르는 키를 가린다 */
export function has(key: string): key is StringKey {
  return key in ko;
}
