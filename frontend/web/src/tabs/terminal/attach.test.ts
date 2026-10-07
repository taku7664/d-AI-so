import { describe, expect, it } from 'vitest';
import { quotePaths, where } from './attach';

describe('where', () => {
  const room = String.raw`C:\Users\me\GitHub\JBroEngine`;

  it('방 폴더 안이면 방 폴더 이름부터 줄여 보인다', () => {
    expect(where(String.raw`${room}\source\JBroEngine\Build\x64\Debug\Localization\ko-KR.yaml`, room)).toBe(
      String.raw`JBroEngine\…\Localization`,
    );
    expect(where(String.raw`${room}\README.md`, room)).toBe('JBroEngine');
    expect(where(String.raw`${room}\docs\a.md`, room)).toBe(String.raw`JBroEngine\docs`);
  });

  it('방 폴더 밖이면 드라이브부터', () => {
    expect(where(String.raw`F:\AI\Temp\shot.png`, room)).toBe(String.raw`F:\…\Temp`);
    expect(where(String.raw`C:\Users\me\GitHub\JBroEngine2\a.txt`, room)).toBe(String.raw`C:\…\JBroEngine2`);
  });
});

describe('quotePaths', () => {
  it('빈칸 있는 경로만 따옴표로 감싸고 앞뒤에 빈칸을 둔다', () => {
    expect(quotePaths([String.raw`C:\a.txt`, String.raw`C:\My Files\b.txt`])).toBe(
      String.raw` C:\a.txt "C:\My Files\b.txt" `,
    );
  });
});
