using UnityEngine;

namespace AIDungeon.Director
{
    /// <summary>
    /// API 지연(3초 초과)·스키마 검증 실패 시 사용하는 결정론 폴백.
    /// 로컬 프로파일로 composition/topology/difficulty를 계산하고, tone+composition 조합에
    /// 맞는 고정 대사를 붙인다 → 폴백조차 플레이어 스타일에 반응하는 것처럼 보인다.
    /// </summary>
    public static class FallbackPresets
    {
        /// <summary>API 없이 프로파일만으로 완결된 결정을 만든다.</summary>
        public static DirectorDecision Build(PlayerProfile p)
        {
            var d = new DirectorDecision
            {
                composition = DirectorPolicy.CanonicalComposition(p),
                topology = DirectorPolicy.CanonicalTopology(p),
                difficultyModifier = DirectorPolicy.CanonicalDifficulty(p),
                tone = DirectorPolicy.CanonicalTone(p),
                fromFallback = true,
            };
            d.analysis = AnalysisFor(d);
            return d;
        }

        /// <summary>tone × composition 조합별 고정 대사. (설계 문서 3.4 프리셋 표)</summary>
        public static string AnalysisFor(DirectorDecision d)
        {
            if (d.tone == Tone.Concern)
                return "지쳐 보이는군요. 잠시 숨을 고르시죠.";

            switch (d.composition)
            {
                case Composition.KiterPack:
                    return d.tone == Tone.Taunt
                        ? "칼잡이시군요. 거리를 벌려드리죠."
                        : "가까이 붙는 걸 좋아하시니, 닿지 못하게 해보겠습니다.";
                case Composition.RusherPack:
                    return d.tone == Tone.Taunt
                        ? "멀리서 쏘시는군요. 코앞까지 보내드리죠."
                        : "거리를 두시는군요. 그 간격을 지워보죠.";
                case Composition.TankBait:
                    return "정면돌파를 좋아하시는군요. 벽을 세워두죠.";
                default: // Balanced
                    return d.tone == Tone.Impressed
                        ? "제법이군요. 판을 다시 짜야겠습니다."
                        : "흥미롭군요. 좀 더 지켜보죠.";
            }
        }
    }
}
