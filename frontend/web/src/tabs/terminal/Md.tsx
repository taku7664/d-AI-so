// 답 말풍선의 마크다운 조금: 문단, # 제목, - · 1. 목록, ``` 코드 블록, **굵게**, `코드`. HTML 로 넣지 않고 요소로 만든다(본문은 도구가 쓴 글이다)
import { Fragment, type ReactNode } from 'react';

function inline(text: string, key: string): ReactNode[] {
  // `코드` 와 **굵게** 만 가른다. 나머지는 글자 그대로
  const parts = text.split(/(`[^`]+`|\*\*[^*]+\*\*)/g);
  return parts.map((part, i) => {
    if (part.startsWith('`') && part.endsWith('`') && part.length > 2)
      return <code key={`${key}-${i}`}>{part.slice(1, -1)}</code>;
    if (part.startsWith('**') && part.endsWith('**') && part.length > 4)
      return <b key={`${key}-${i}`}>{part.slice(2, -2)}</b>;
    return <Fragment key={`${key}-${i}`}>{part}</Fragment>;
  });
}

const LIST = /^\s*(?:[-*]|\d+\.)\s+/;
const HEADING = /^#{1,6}\s+/;

export function Md({ text }: { text: string }) {
  const blocks: ReactNode[] = [];
  const lines = text.replace(/\r\n/g, '\n').split('\n');
  let para: string[] = [];
  let list: string[] = [];
  const flushPara = () => {
    if (para.length)
      blocks.push(
        <p key={blocks.length}>
          {para.map((line, i) => (
            <Fragment key={i}>
              {i > 0 && <br />}
              {inline(line, `${blocks.length}-${i}`)}
            </Fragment>
          ))}
        </p>,
      );
    para = [];
  };
  const flushList = () => {
    if (list.length)
      blocks.push(
        <ul key={blocks.length}>
          {list.map((line, i) => (
            <li key={i}>{inline(line, `${blocks.length}-${i}`)}</li>
          ))}
        </ul>,
      );
    list = [];
  };

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] ?? '';
    if (line.trimStart().startsWith('```')) {
      flushPara();
      flushList();
      const code: string[] = [];
      for (i++; i < lines.length && !(lines[i] ?? '').trimStart().startsWith('```'); i++) code.push(lines[i] ?? '');
      blocks.push(<pre key={blocks.length}>{code.join('\n')}</pre>);
      continue;
    }
    if (!line.trim()) {
      flushPara();
      flushList();
    } else if (HEADING.test(line)) {
      flushPara();
      flushList();
      blocks.push(
        <p className="h" key={blocks.length}>
          {inline(line.replace(HEADING, ''), `${blocks.length}`)}
        </p>,
      );
    } else if (LIST.test(line)) {
      flushPara();
      list.push(line.replace(LIST, ''));
    } else {
      flushList();
      para.push(line);
    }
  }
  flushPara();
  flushList();
  return <>{blocks}</>;
}
