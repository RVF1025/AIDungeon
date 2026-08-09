# AIDungeon

당신이 싸우는 방식을 실시간으로 읽고, 그에 맞춰 적·전장·갈림길을 설계하는 성격 있는 AI 디렉터와 겨루는 탑다운 2D 로그라이크.

## ▶ 웹에서 바로 플레이

https://rvf1025.github.io/AIDungeon/

설치 없이 브라우저에서 바로 실행됩니다. (데스크톱 Chrome 권장, 키보드+마우스)

## 조작

- 이동: `W` `A` `S` `D`
- 근접 공격: 마우스 좌클릭
- 원거리 공격: 마우스 우클릭
- 조준: 마우스(크로스헤어)
- 갈림길 선택: 숫자 키 `1`/`2`/`3` 또는 마우스 클릭

## 목표

층을 계속 내려가며 최대한 깊이 도달하기. AI 디렉터가 매 층 직전 전투를 분석해 플레이어를 카운터하는 적과 전장을 설계하므로, 늘 같은 방식으로는 오래 버티기 어렵습니다. 체력이 0이 되면 게임 오버, 도달한 층수가 기록입니다.

## AI 디렉터

- 플레이어의 근접/원거리 편중·공격성·체력을 읽어 적 구성과 전장을 카운터
- 성격이 다른 3인의 디렉터(오만한 귀족 / 광기의 어릿광대 / 처형자)가 매 런 무작위 배정되어 갈림길을 구성하고 대사로 해설·도발
- AI가 평가하는 갈림길, 정예 전투와 ??? 방의 리스크/리턴

수치 밸런스는 결정론 코드가 소유하고, LLM(Google Gemini)은 캐릭터 대사·평가만 담당하는 하이브리드 구조입니다. 자세한 내용은 [AI 활용 기술 문서](submission/ai-tech-doc.pdf) 참고.

## 링크

- 플레이(웹): https://rvf1025.github.io/AIDungeon/
- 플레이 영상: https://youtu.be/NKwZPFdqlUY
- 게임 소개 문서: [submission/game-intro.pdf](submission/game-intro.pdf)
- AI 기술 문서: [submission/ai-tech-doc.pdf](submission/ai-tech-doc.pdf)

## 기술

Unity 6.3 (WebGL) · Google Gemini (AI 디렉터, Vercel 서버리스 프록시 경유) · GitHub Pages

에셋: Kenney Tiny Dungeon (CC0) · 나눔고딕 (SIL OFL)
