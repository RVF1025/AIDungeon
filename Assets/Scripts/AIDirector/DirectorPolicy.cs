using UnityEngine;

namespace AIDungeon.Director
{
    /// <summary>
    /// 결정론 정책 계층. 실측 결과 LLM은 수치 임계값 매핑을 이따금 틀렸지만(원거리 플레이어에
    /// kiter 배치 등), 규칙 코드는 100% 정확·0ms·0원이다. 그래서 composition/topology/difficulty는
    /// 여기서 계산하고, AI 출력은 <see cref="Reconcile"/>로 이 규범에 맞춘다.
    /// AI에게는 대사(analysis)와 태도(tone)만 맡긴다.
    /// (설계 문서 3.3 매핑 테이블과 동일한 임계값)
    /// </summary>
    public static class DirectorPolicy
    {
        // composition ← meleeRatio
        public static string CanonicalComposition(PlayerProfile p)
        {
            if (p.meleeRatio >= 0.6f) return Composition.KiterPack;   // 근접 플레이어 → 원거리로 농락
            if (p.meleeRatio <= 0.4f) return Composition.RusherPack;  // 원거리 플레이어 → 근접 압박
            if (p.aggression >= 0.7f) return Composition.TankBait;    // 애매+저돌 → 탱커 미끼
            return Composition.Balanced;
        }

        // topology ← aggression (원거리형은 cover로 라인 차단 우선)
        public static string CanonicalTopology(PlayerProfile p)
        {
            if (p.aggression >= 0.65f) return Topology.Open;      // 저돌 → 개활지로 유인
            if (p.aggression <= 0.35f) return Topology.Encircle;  // 회피/카이팅 → 포위
            if (p.meleeRatio <= 0.4f) return Topology.Cover;      // 원거리형 → 엄폐로 사선 차단
            return Topology.Corridor;
        }

        /// <summary>
        /// 방 유형 선택. 기본은 프로파일 기반(CanonicalTopology)이지만, 3층마다 유형을 순환시켜
        /// 엄폐(cover)·개활(open)·포위(encircle)·통로(corridor)가 골고루 등장하게 한다.
        /// (composition=적 구성은 여전히 플레이어를 카운터하고, topology=방 형태만 변주)
        /// </summary>
        public static string ChooseTopology(PlayerProfile p, int floor)
        {
            // 짝수 층마다 방 유형을 순환(2층부터 cover 시작) → 모든 방이 골고루·자주 등장.
            // 홀수 층은 프로파일 기반(플레이어를 읽는 느낌 유지).
            if (floor % 2 == 0)
            {
                string[] rotation = { Topology.Cover, Topology.Open, Topology.Encircle, Topology.Corridor };
                return rotation[((floor / 2) - 1) % rotation.Length];
            }
            return CanonicalTopology(p);
        }

        private static readonly string[] CompOrder =
            { Composition.KiterPack, Composition.RusherPack, Composition.TankBait, Composition.Balanced };
        private static readonly string[] TopoOrder =
            { Topology.Cover, Topology.Open, Topology.Encircle, Topology.Corridor };

        /// <summary>직전 층과 같은 구성이면 다른 걸로 밀어 변화 보장.</summary>
        public static string CompositionAvoiding(PlayerProfile p, string last)
        {
            var c = CanonicalComposition(p);
            if (c != last || string.IsNullOrEmpty(last)) return c;
            int i = System.Array.IndexOf(CompOrder, c);
            return CompOrder[(Mathf.Max(i, 0) + 1) % CompOrder.Length];
        }

        /// <summary>직전 층과 같은 방형태면 다른 걸로 밀어 변화 보장.</summary>
        public static string ChooseTopologyAvoiding(PlayerProfile p, int floor, string last)
        {
            var t = ChooseTopology(p, floor);
            if (t != last || string.IsNullOrEmpty(last)) return t;
            int i = System.Array.IndexOf(TopoOrder, t);
            return TopoOrder[(Mathf.Max(i, 0) + 1) % TopoOrder.Length];
        }

        // difficultyModifier ← avgHpPct (0.8 여유~ 1.3 몰아붙임). 순수 계산, AI는 서사만.
        public static float CanonicalDifficulty(PlayerProfile p)
        {
            return Mathf.Lerp(0.8f, 1.3f, Mathf.Clamp01(p.avgHpPct));
        }

        // 정예(챔피언) 스폰 규칙(스포너·대사 프롬프트 공통 소스). caller가 마릿수는 클램프.
        public const float EliteDiffThreshold = 1.35f;
        public static int EliteCountFor(int floor, float diff)
        {
            int n = 0;
            if (floor >= 3) n = 1;                    // 3층부터 챔피언 1
            if (diff >= EliteDiffThreshold) n++;      // 정예 전투 경로 등 고난이도 +1
            return n;
        }
        public static bool WillSpawnElite(int floor, float diff) => EliteCountFor(floor, diff) > 0;

        // taunt(도발)를 쓸 수 없는 상황의 대체 톤(체력 상황 기반).
        public static string NonTauntTone(PlayerProfile p)
        {
            if (p.avgHpPct <= 0.35f) return Tone.Concern;
            if (p.avgHpPct >= 0.85f) return Tone.Impressed;
            return Tone.Neutral;
        }

        // 대사에 방/지형/위치 단어가 하나라도 있으면 true(정책상 공간 언급 전면 금지).
        // topology는 실제 방으로 이미 보이므로 대사가 묘사할 필요가 없다 → 모순 원천 차단.
        private static readonly string[] SpaceWords =
        {
            "통로", "복도", "개활", "광야", "벌판", "엄폐", "기둥", "차폐", "은폐",
            "포위", "에워", "둘러싸", "틈새", "탁 트", "코너",
        };
        public static bool MentionsSpace(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (var w in SpaceWords)
                if (text.Contains(w)) return true;
            return false;
        }

        // 폴백/검증용 기본 tone (AI가 없을 때만 사용).
        public static string CanonicalTone(PlayerProfile p)
        {
            if (p.avgHpPct <= 0.3f) return Tone.Concern;                       // 고전 → 자비
            if (p.meleeRatio >= 0.8f || p.meleeRatio <= 0.2f) return Tone.Taunt; // 스타일 편중 → 도발
            if (p.avgHpPct >= 0.85f) return Tone.Impressed;                    // 압도 → 감탄
            return Tone.Neutral;
        }

        /// <summary>
        /// AI 출력을 규범과 대조해 보정한다.
        /// trustAiDecisions=false(기본): composition/topology/difficulty를 규범값으로 덮어씀
        ///   → 대사와 실제 방이 100% 일치. analysis/tone은 AI 것을 유지.
        /// trustAiDecisions=true: AI 값을 존중하되 enum/범위만 검사, 이상하면 규범으로 대체.
        /// </summary>
        public static void Reconcile(DirectorDecision d, PlayerProfile p, bool trustAiDecisions)
        {
            if (!trustAiDecisions)
            {
                d.composition = CanonicalComposition(p);
                d.topology = CanonicalTopology(p);
                d.difficultyModifier = CanonicalDifficulty(p);
            }
            else
            {
                if (!IsValidComposition(d.composition)) d.composition = CanonicalComposition(p);
                if (!IsValidTopology(d.topology)) d.topology = CanonicalTopology(p);
                d.difficultyModifier = Mathf.Clamp(d.difficultyModifier, 0.8f, 1.3f);
            }

            if (!IsValidTone(d.tone)) d.tone = CanonicalTone(p);
            if (string.IsNullOrWhiteSpace(d.analysis)) d.analysis = FallbackPresets.AnalysisFor(d);
        }

        public static bool IsValidComposition(string s) =>
            s == Composition.KiterPack || s == Composition.RusherPack ||
            s == Composition.TankBait || s == Composition.Balanced;

        public static bool IsValidTopology(string s) =>
            s == Topology.Encircle || s == Topology.Cover ||
            s == Topology.Open || s == Topology.Corridor;

        public static bool IsValidTone(string s) =>
            s == Tone.Taunt || s == Tone.Impressed ||
            s == Tone.Concern || s == Tone.Neutral;
    }
}
