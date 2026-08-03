# Gemini 프록시 배포

WebGL은 브라우저라 (1) Gemini 직접 호출은 CORS로 막히고, (2) API 키가 노출된다. 그래서 프록시가 필요하다.

## ⚠️ Cloudflare Workers는 쓰지 말 것
CF Workers는 egress(나가는) 지역이 요청마다 달라져, Gemini가 `User location is not supported`를
간헐적으로 뱉는다(실측 성공률 ~1/3). `worker.js`는 참고용으로 남겨두지만 **실사용 금지**.

## ✅ Vercel로 배포 (미국 리전 고정 → 위치 에러 없음, 무료, 로컬 툴 불필요)

### 1. Vercel 프로젝트 생성
1. https://vercel.com → **Continue with GitHub**로 가입/로그인
2. **Add New… > Project** → GitHub의 `AIDungeon` 리포 **Import**
3. **Root Directory**를 `proxy` 로 지정 (Edit 눌러 선택) — Unity 본체 말고 프록시만 배포
4. **Framework Preset = Other**, Build/Output 설정은 비워둠
5. **Environment Variables**에 추가: Name `GEMINI_API_KEY`, Value = 본인 Gemini 키
6. **Deploy**

### 2. 엔드포인트 확인
배포되면 주소는 `https://<프로젝트명>.vercel.app` 형태. 함수는
`https://<프로젝트명>.vercel.app/api/gemini` 에 있다.

### 3. 동작 확인
```bash
curl -s -X POST "https://<프로젝트명>.vercel.app/api/gemini" \
  -H "Content-Type: application/json" \
  -d '{"contents":[{"parts":[{"text":"Reply exactly: OK"}]}]}'
```
→ `OK` 오면 성공.

### 4. Unity 연결
`GeminiDirectorClient`의 `proxyUrl`에 `https://<프로젝트명>.vercel.app/api/gemini` 입력.
(모델 바꾸려면 `?model=gemini-flash-latest` 쿼리 추가 가능)

## 보안 메모
- 키는 Vercel 환경변수에만. 코드/깃엔 안 들어감(`.gitignore`가 `.env` 차단).
- 지금 키는 채팅에 노출됐으니 **최종 공개 전 재발급** 권장.
