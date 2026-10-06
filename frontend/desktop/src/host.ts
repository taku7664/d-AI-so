// Daiso.Host 를 자식 프로세스로 띄우고, 표준 출력의 주소 알림을 기다린다 (docs/SECURITY.md "서버를 띄우는 순서")
import { spawn, type ChildProcess } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import readline from 'node:readline';

export interface RunningHost {
  readonly process: ChildProcess;
  /** `http://127.0.0.1:{port}`. 창이 이 출처 밖으로 나가지 않게 막을 때 쓴다 */
  readonly origin: string;
  /** 첫 접속 주소. 토큰이 붙어 있어 로그에 남기지 않는다 */
  readonly openUrl: string;
}

const LISTENING = 'DAISO_LISTENING ';
const START_TIMEOUT_MS = 30_000;
const ORIGIN = /^http:\/\/127\.0\.0\.1:\d{1,5}$/;

export function startHost(exe: string): Promise<RunningHost> {
  // 실행할 때마다 새로 만든다. Host 는 32자 미만이면 뜨지 않는다 (base64url 43자)
  const token = randomBytes(32).toString('base64url');
  const child = spawn(exe, [], {
    env: { ...process.env, DAISO_TOKEN: token, DAISO_PARENT_PID: String(process.pid) },
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
    // Windows 에서 libuv 는 자식을 "부모가 죽으면 같이 죽이는" 잡 오브젝트에 넣는다. 그러면 앱을 끌 때 Host 가 정리할 틈 없이
    // 죽어 server.json 이 남는다. 잡에서 빼고, 끝내는 일은 Host 의 부모 감시(ParentWatcher)에 맡긴다
    detached: true,
  });

  // 파이프를 비우지 않으면 버퍼가 차서 Host 가 멈춘다. 그대로 흘려보낸다
  child.stderr.on('data', (chunk: Buffer) => process.stderr.write(chunk));

  return new Promise((resolve, reject) => {
    let settled = false;
    const fail = (error: Error): void => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      child.kill();
      reject(error);
    };
    const onExit = (code: number | null): void => fail(new Error(`서버가 주소를 알리기 전에 끝났다 (코드 ${code})`));
    const timer = setTimeout(
      () => fail(new Error(`서버가 ${START_TIMEOUT_MS / 1000}초 안에 주소를 알리지 않았다`)),
      START_TIMEOUT_MS,
    );

    child.once('error', fail);
    child.once('exit', onExit);
    readline.createInterface({ input: child.stdout }).on('line', (line) => {
      process.stdout.write(line + '\n');
      if (settled || !line.startsWith(LISTENING)) return;

      const origin = line.slice(LISTENING.length).trim();
      if (!ORIGIN.test(origin)) {
        fail(new Error(`서버가 알린 주소를 알아볼 수 없다: ${origin}`));
        return;
      }
      settled = true;
      clearTimeout(timer);
      child.off('exit', onExit);
      resolve({ process: child, origin, openUrl: `${origin}/?token=${token}` });
    });
  });
}
