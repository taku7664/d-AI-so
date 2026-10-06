// 문구 키 검사. 쓰지 않는 키와 빈 문구를 잡는다. 없는 키는 t() 의 타입이 잡는다
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import ko from './ko.json';

const SRC = join(import.meta.dirname, '..');

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return sources(path);
    return /\.(ts|tsx)$/.test(name) && !name.endsWith('.test.ts') && !name.endsWith('.gen.ts') ? [path] : [];
  });
}

describe('ko.json', () => {
  const code = sources(SRC)
    .map((path) => readFileSync(path, 'utf8'))
    .join('\n');

  it('모든 키를 코드 어딘가에서 쓴다', () => {
    // t('key') 로 바로 쓰거나, 탭 제목처럼 `tab.${id}` 로 조합해 쓴다. 조합하는 접두사는 여기 적어 둔다
    const composed = [
      'tab.',
      'usage.grain.',
      'role.',
      'account.state.',
      'account.done.',
      'authNote.',
      'terminal.state.',
    ];
    const unused = Object.keys(ko).filter(
      (key) => !code.includes(`'${key}'`) && !composed.some((prefix) => key.startsWith(prefix)),
    );
    expect(unused).toEqual([]);
  });

  it('빈 문구가 없다', () => {
    expect(Object.entries(ko).filter(([, text]) => text.trim() === '')).toEqual([]);
  });
});
