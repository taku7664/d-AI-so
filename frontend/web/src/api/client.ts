// 서버 요청은 여기서 만든 클라이언트로만 한다. 타입은 서버 OpenAPI 문서에서 만든 schema.gen.ts 에서 온다 (docs/ARCHITECTURE.md)
// 화면과 서버는 같은 출처다. 쿠키는 브라우저가 싣는다
import createClient from 'openapi-fetch';
import type { components, paths } from './schema.gen';

export const api = createClient<paths>({ baseUrl: '' });

export type Schemas = components['schemas'];
