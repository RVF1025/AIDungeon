# AI Dungeon — 셋업 & 진행 현황

개인 프로젝트. Unity 6.3 / WebGL / Gemini API.
설계 문서는 `Downloads/design-doc.md` 참조.

---

## 밥 먹는 동안 해둔 것 (자동 밑작업)

### 검증으로 확정한 사실
- **모델 = `gemini-flash-lite-latest`** (현재 실체 = `gemini-3.5-flash-lite`).
  - 레이턴시 단발 **~1.0~1.9초** → 층 전환 암전 2초 안에 은폐 가능. ✅
  - 단, 무료 티어는 **버스트(연타) 시 14~31초까지 스파이크**함(연속 테스트에서 관측). 실제 게임은
    층당 1회 호출이라 정상 범위지만, 영상 촬영 땐 안정성 위해 결제 연결(종량제) 권장. 튀어도 3초 폴백이 커버.
  - 실측 대사 품질 우수: 전술(kiter/cover/tank_bait 등)을 대사에 정확히 반영함.
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

## 진행 로그
- ✅ **프록시 왕복 뚫림** (Vercel). Director 요청 3/3 성공, ~1.1초, 구조화 JSON 정상.
  Cloudflare는 egress 지역 뽑기로 실패 → Vercel(미국 리전 고정)로 교체함.
- GitHub 원격 = `RVF1025/AIDungeon` (커밋 저자도 RVF1025로 정리).

## 네가 돌아와서 할 것 (순서대로)

1. **Vercel 고정 주소 확보** — preview 해시 주소 말고 **프로덕션 도메인**(예:
   `https://ai-dungeon-rvf1.vercel.app`) + `/api/gemini`. Vercel 프로젝트 Domains에서 확인.
2. **Unity 연결** — 씬에 빈 GameObject 만들고 `GeminiDirectorClient` + `AIDirectorTester` 추가,
   인스펙터 `proxyUrl`에 위 프로덕션 주소 입력.
3. **Play** — Console에 4개 샘플의 판단 결과가 찍히면 왕복 성공. (오프라인 확인은 컴포넌트
   우클릭 > `TestFallback`)
4. **WebGL 모듈 설치 완료 확인** — 이후 Web 플랫폼으로 빌드해 브라우저에서 CORS까지 재검증.
5. **키 재발급** — 지금 키는 채팅에 노출됐음. 최종 공개 전 AI Studio에서 Revoke 후 새 키로
   Vercel 환경변수 교체.

> git: 로컬 리포 초기화됨(`.gitignore` 포함, Library 제외). GitHub 원격 연결은 리포 만들면 연결해줌.
