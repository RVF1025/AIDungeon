using System.Collections.Generic;
using UnityEngine;

namespace AIDungeon.Director
{
    /// <summary>
    /// 갈림길 선택지 유형(코드 소유·검증됨). 제목/설명은 고정 문구. AI는 '어느 유형이 나올지'가 아니라
    /// 제시된 갈림길을 한 문장으로 '평가'만 한다(선택 가중치는 코드가 성향·상황으로 계산).
    ///   diffMul  : 다음 층 난이도 배수
    ///   countMul : 적 수 배수
    ///   heal01   : 진입 시 회복량(최대 체력 대비)
    ///   combat   : 전투 방인지(휴식은 false → 전투 없이 회복 후 다음 갈림길)
    ///   treasure : 클리어 후 보물상자 지급
    ///   mystery  : ??? 방(진입 시 무작위 결과로 해석)
    /// </summary>
    public class ForkArchetype
    {
        public string id;
        public string title;
        public string desc;
        public float diffMul = 1f;
        public float countMul = 1f;
        public float heal01 = 0f;
        public bool combat = true;
        public bool treasure = false;
        public bool mystery = false;
    }

    /// <summary>AI가 제시된 갈림길을 평가한 한 문장(초상 표정용 tone 포함).</summary>
    public class ForkComment
    {
        public string line;
        public string tone;
    }

    /// <summary>??? 방이 진입 시 해석된 결과.</summary>
    public class MysteryOutcome
    {
        public float diffMul = 1f;
        public float countMul = 1f;
        public bool treasure = false;
        public bool elite = false;    // 정예 매복이면 정예 스폰
        public bool combat = true;    // 보물방 등은 전투 없음
        public string reveal;         // 정체(내용) — AI가 어조로 각색해 전달
    }

    public static class ForkArchetypes
    {
        public static readonly ForkArchetype Normal = new()
        {
            id = "normal", title = "일반 전투", desc = "평범한 난이도의 전투", diffMul = 1f,
        };
        public static readonly ForkArchetype Elite = new()
        {
            id = "elite", title = "정예 전투", desc = "정예 몬스터와 더 좋은 보상",
            diffMul = 1.3f, treasure = true,
        };
        public static readonly ForkArchetype Rest = new()
        {
            id = "rest", title = "휴식 공간", desc = "체력 40% 회복",
            combat = false, heal01 = 0.4f,
        };
        public static readonly ForkArchetype Mystery = new()
        {
            id = "mystery", title = "???", desc = "무엇이 나올지 알 수 없다", mystery = true,
        };

        public static readonly ForkArchetype[] All = { Normal, Elite, Rest, Mystery };

        public static ForkArchetype ById(string id)
        {
            foreach (var a in All) if (a.id == id) return a;
            return Normal;
        }

        // 페르소나 id(DirectorPersonas와 일치).
        private const string Aristocrat = "aristocrat", Jester = "jester", Executioner = "executioner";

        /// <summary>
        /// 이번 갈림길에 제시할 유형 3종을 성향·상황 가중치로 뽑는다(중복 없음).
        /// 휴식은 직전 노드가 휴식이면 제외(연속 등장 금지).
        /// </summary>
        public static List<ForkArchetype> Select(string personaId, float avgHpPct, string lastArchId)
        {
            var pool = new List<ForkArchetype>(All);
            var weights = new List<float>(pool.Count);
            foreach (var a in pool) weights.Add(Weight(a, personaId, avgHpPct, lastArchId));

            var chosen = new List<ForkArchetype>(3);
            for (int k = 0; k < 3 && pool.Count > 0; k++)
            {
                int i = WeightedIndex(weights);
                if (i < 0) break;
                chosen.Add(pool[i]);
                pool.RemoveAt(i); weights.RemoveAt(i);
            }
            return chosen;
        }

        private static float Weight(ForkArchetype a, string persona, float hp, string lastArchId)
        {
            if (a.id == Rest.id && lastArchId == Rest.id) return 0f; // 연속 휴식 금지

            float w = 1f;
            switch (a.id)
            {
                case "rest":
                    w *= 1f + (1f - Mathf.Clamp01(hp)) * 2.5f;       // 체력 낮을수록 ↑
                    if (persona == Aristocrat) w *= 1.6f;             // 귀족: 자비 성향
                    else if (persona == Executioner) w *= 0.35f;     // 처형자: 잘 안 줌
                    else if (persona == Jester) w *= 0.8f;
                    break;
                case "elite":
                    if (persona == Executioner) w *= 2.0f;           // 처형자: 강적 선호
                    else if (persona == Aristocrat) w *= 0.9f;
                    break;
                case "mystery":
                    if (persona == Jester) w *= 2.5f;                // 광대: 예측불가 선호
                    else w *= 0.7f;
                    break;
            }
            return Mathf.Max(0f, w);
        }

        private static int WeightedIndex(List<float> weights)
        {
            float total = 0f;
            foreach (var w in weights) total += w;
            if (total <= 0f) return -1;
            float roll = Random.value * total;
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f) return i;
            }
            return weights.Count - 1;
        }

        /// <summary>??? 방 진입 시 무작위 결과 해석(광대다운 예측불가).</summary>
        public static MysteryOutcome ResolveMystery()
        {
            int r = Random.Range(0, 4);
            switch (r)
            {
                case 0: return new MysteryOutcome { diffMul = 1.3f, treasure = true, elite = true, reveal = "정예의 매복이었다!" };
                case 1: return new MysteryOutcome { diffMul = 0.9f, countMul = 1.6f, reveal = "적 떼의 습격이다!" };
                case 2: return new MysteryOutcome { diffMul = 1.15f, reveal = "함정이 도사리고 있었다." };
                default: return new MysteryOutcome { combat = false, treasure = true, reveal = "뜻밖의 보물을 발견했다!" };
            }
        }
    }
}
