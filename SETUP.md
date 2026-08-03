# AI Dungeon — 셋업 & 진행 현황

NAN 2026 (NHN Game x AI Hackathon) 예선. Unity 6.3 / WebGL / Gemini API.
설계 문서는 `Downloads/design-doc.md` 참조.

---

## 밥 먹는 동안 해둔 것 (자동 밑작업)

### 검증으로 확정한 사실
- **모델 = `gemini-flash-lite-latest`** (현재 실체 = `gemini-3.5-flash-lite`).
  - 레이턴시 **~1.2초** → 층 전환 암전 2초 안에 은폐 가능. ✅
  - `gemini-flash-latest`(full)는 **~6초**라 탈락. thinking을 끌 수도 없다(`thinkingBudget:0` → 400).
  - `gemini-2.5-flash`는 신규 사용자에게 닫힘(404). 3.x 세대로 가야 함.
- **신뢰성**: 애매한 프롬프트 + temp 0.9에선 LLM이 카운터 로직을 틀렸다(원거리 플레이어에 kiter 배치).
  수치 임계값을 명시하고 temp를 낮추니 5/5 정확. → **아키텍처 결정**(아래).
- API 키 형식이 `AQ.`로 시작(구형 `AIza`가 아님). 정상 동작 확인함.

### 아키텍처 결정 — 전술은 코드, 서사는 AI
LLM이 수치 매핑을 이따금 틀리므로, 신뢰성/속도/비용을 위해 역할을 나눴다:
- **결정론 C# (`DirectorPolicy`)**: composition / topology / difficultyModifier 계산. 100% 정확·0ms·0원.
- **AI (flash-lite)**: 그 전술을 암시하는 **대사(analysis) + 태도(tone)**만 생성. LLM만 할 수 있는 부분.
- 결과: 대사 ↔ 실제 방이 100% 일치. 출력 토큰↓ → 더 빠름.
- `GeminiDirectorClient.letAiDecideTactics = true`로 바꾸면 전술도 AI에게 위임(검증만) — 원하면 실험 가능.

### 만들어둔 파일
```
proxy/worker.js            Cloudflare Worker (CORS + 키주입 + 언랩 + 3s 타임아웃)
proxy/DEPLOY.md            프록시 배포 절차 (툴체인 설치 불필요)
Assets/Scripts/AIDirector/
  PlayerProfile.cs         입력 (meleeRatio/aggression/avgHpPct)
  DirectorDecision.cs      출력 + enum 상수
  DirectorPolicy.cs        결정론 매핑 + 검증/보정(Reconcile)
  FallbackPresets.cs       오프라인 폴백 + 조합별 고정 대사
  GeminiDirectorClient.cs  프록시 왕복(코루틴, WebGL 호환)
  AIDirectorTester.cs      Play 누르면 샘플 프로파일로 왕복 테스트
```

---

## 네가 돌아와서 할 것 (순서대로)

1. **키 재발급 권장** — 지금 키는 채팅에 노출됐음. 최종 공개 전 AI Studio에서 Revoke 후 새 키.
2. **프록시 배포** — `proxy/DEPLOY.md` 따라 Cloudflare Worker 생성 + `GEMINI_API_KEY` Secret 등록.
3. **Unity 연결** — 씬에 빈 GameObject 만들고 `GeminiDirectorClient` + `AIDirectorTester` 추가,
   인스펙터 `proxyUrl`에 Worker 주소 입력.
4. **Play** — Console에 4개 샘플의 판단 결과가 찍히면 왕복 성공. (오프라인 확인은 컴포넌트
   우클릭 > `TestFallback`)
5. **WebGL 모듈 설치 완료 확인** — 이후 Web 플랫폼으로 빌드해 브라우저에서 CORS까지 재검증.

> git: 로컬 리포 초기화됨(`.gitignore` 포함, Library 제외). GitHub 원격 연결은 리포 만들면 연결해줌.
