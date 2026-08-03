# Gemini 프록시 배포 (Cloudflare Workers, 툴체인 설치 불필요)

WebGL은 브라우저라서 (1) Gemini에 직접 호출하면 CORS로 막히고, (2) API 키가 클라이언트에 노출된다.
그래서 경량 프록시를 한 개 둔다. node/wrangler 없이 **대시보드 붙여넣기**로 배포한다.

## 1. Worker 생성
1. https://dash.cloudflare.com → **Workers & Pages** → **Create** → **Create Worker**
2. 이름 예: `ai-dungeon-proxy` → **Deploy** (기본 템플릿으로 일단 배포)
3. **Edit code** → 기존 내용 전부 지우고 [`worker.js`](worker.js) 내용 붙여넣기 → **Deploy**

## 2. API 키를 Secret으로 등록 (코드/깃에 절대 안 넣음)
1. Worker 페이지 → **Settings** → **Variables and Secrets**
2. **Add** → Type: **Secret** → Name: `GEMINI_API_KEY` → Value: 본인 Gemini 키 → Save
3. (선택) 기본 모델 바꾸려면 일반 Variable로 `MODEL` 추가 (기본 `gemini-flash-lite-latest`)

## 3. 동작 확인
배포된 주소는 `https://ai-dungeon-proxy.<계정>.workers.dev` 형태다.
아래로 테스트(주소만 본인 걸로 교체):

```bash
curl -s -X POST "https://ai-dungeon-proxy.<계정>.workers.dev" \
  -H "Content-Type: application/json" \
  -d '{"contents":[{"parts":[{"text":"Reply with exactly: OK"}]}]}'
```
→ `OK` 비슷하게 오면 성공. (스키마 붙인 실제 요청은 Unity가 보냄)

## 4. Unity에 주소 연결
`Assets/Scripts/AIDirector/GeminiDirectorClient.cs`의 인스펙터 필드 `proxyUrl`에
위 Worker 주소를 넣는다. (모델 바꾸려면 `?model=gemini-flash-latest`처럼 쿼리 추가 가능)

## 보안 메모
- 지금 쓰는 키는 채팅에 한 번 노출됐으니 **최종 공개 배포 전 AI Studio에서 Revoke 후 재발급** 권장.
- 공개 Worker는 아무나 호출 가능하니, 제출 후 방치할 거면 Worker에 간단한 Referer/Origin 체크나
  Rate limit을 추가해도 좋다(예선 데모엔 없어도 무방).
