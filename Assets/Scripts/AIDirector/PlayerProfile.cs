using System;
using System.Globalization;

namespace AIDungeon.Director
{
    /// <summary>
    /// AI Director 입력. 각 층 클리어 시점에 이동평균으로 갱신(최근 층 가중 ↑).
    /// 설계 원칙: 입력 축 분리 — 하나의 축이 하나의 출력만 담당한다.
    ///   meleeRatio  → 적 구성(composition)
    ///   aggression  → 방 배치(topology)
    ///   avgHpPct    → 난이도(difficultyModifier)
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        /// <summary>근접↔원거리. 0=순수 원거리, 1=순수 근접 (근접/원거리 데미지 비중).</summary>
        public float meleeRatio;

        /// <summary>저돌↔회피. 0=카이팅/회피, 1=적극 교전 (먼저 거리 좁힌 빈도 / 카이팅 시간).</summary>
        public float aggression;

        /// <summary>실력 신호. 0=계속 빈사, 1=여유롭게 클리어 (층 평균 잔여 HP + 클리어 속도).</summary>
        public float avgHpPct;

        public PlayerProfile() { }

        public PlayerProfile(float meleeRatio, float aggression, float avgHpPct)
        {
            this.meleeRatio = meleeRatio;
            this.aggression = aggression;
            this.avgHpPct = avgHpPct;
        }

        /// <summary>Gemini 요청에 넣을 한 줄 표현(소수점은 InvariantCulture로 고정).</summary>
        public string ToPromptLine()
        {
            var c = CultureInfo.InvariantCulture;
            return string.Format(c, "meleeRatio={0:0.00}, aggression={1:0.00}, avgHpPct={2:0.00}",
                meleeRatio, aggression, avgHpPct);
        }
    }
}
