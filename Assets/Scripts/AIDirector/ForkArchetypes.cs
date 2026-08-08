using System.Collections.Generic;
using System.Text;

namespace AIDungeon.Director
{
    /// <summary>
    /// 갈림길 선택지의 '기계적 유형'(코드 소유·검증됨). AI는 이 풀에서 상황·성향에 맞게 고르고
    /// 제목/설명/대사만 성향 말투로 작성한다. 수치는 절대 AI가 못 건드림 → 밸런스 안 깨짐.
    ///   diffMul  : 다음 층 난이도 배수(정예 등장 임계 1.35와 연동)
    ///   countMul : 적 수 배수
    ///   heal01   : 진입 전 회복량(최대 체력 대비, 0=없음)
    /// </summary>
    public class ForkArchetype
    {
        public string id;
        public string title;      // 기본 제목(AI 미작성 시 폴백)
        public string desc;       // 기본 설명(폴백)
        public string meaning;    // 프롬프트용 유형 의미(AI가 고를 근거)
        public float diffMul = 1f;
        public float countMul = 1f;
        public float heal01 = 0f;
    }

    public static class ForkArchetypes
    {
        public static readonly ForkArchetype Normal = new()
        {
            id = "normal", title = "평범한 전투", desc = "무난한 다음 방",
            meaning = "표준 난이도 전투", diffMul = 1f,
        };
        public static readonly ForkArchetype Elite = new()
        {
            id = "elite", title = "정예 전투", desc = "강적(정예)이 기다린다 · 난이도↑",
            meaning = "강한 소수(정예 등장), 고난이도·고위험", diffMul = 1.3f,
        };
        public static readonly ForkArchetype Horde = new()
        {
            id = "horde", title = "물량전", desc = "약하지만 수많은 적",
            meaning = "약한 적 다수의 물량 압박", diffMul = 0.9f, countMul = 1.6f,
        };
        public static readonly ForkArchetype Rest = new()
        {
            id = "rest", title = "휴식", desc = "체력 회복 + 가벼운 전투",
            meaning = "회복 후 소수의 적만 상대하는 한숨 돌리는 길", diffMul = 1f, countMul = 0.6f, heal01 = 0.4f,
        };

        public static readonly ForkArchetype[] All = { Normal, Elite, Horde, Rest };

        public static ForkArchetype ById(string id)
        {
            foreach (var a in All) if (a.id == id) return a;
            return Normal;
        }

        public static bool IsValidId(string id)
        {
            foreach (var a in All) if (a.id == id) return true;
            return false;
        }

        /// <summary>프롬프트에 넣을 유형 메뉴(AI가 선택 근거로 삼음).</summary>
        public static string Menu()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < All.Length; i++)
            {
                if (i > 0) sb.Append("; ");
                sb.Append(All[i].id).Append(':').Append(All[i].meaning);
            }
            return sb.ToString();
        }

        /// <summary>스키마 enum용 id 목록("normal","elite",...).</summary>
        public static string IdEnumJson()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < All.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(All[i].id).Append('"');
            }
            return sb.ToString();
        }

        /// <summary>AI 실패/무효 시 쓸 기본 3종(코드 저작).</summary>
        public static List<ForkChoice> DefaultChoices()
        {
            return new List<ForkChoice>
            {
                new() { id = Normal.id, title = Normal.title, desc = Normal.desc, line = "", tone = Tone.Neutral },
                new() { id = Elite.id,  title = Elite.title,  desc = Elite.desc,  line = "", tone = Tone.Neutral },
                new() { id = Rest.id,   title = Rest.title,   desc = Rest.desc,   line = "", tone = Tone.Neutral },
            };
        }
    }

    /// <summary>AI가 설계한 갈림길 선택지 하나(유형 id + 성향 말투 저작 텍스트).</summary>
    public class ForkChoice
    {
        public string id;      // ForkArchetypes 풀의 유형 id(검증됨)
        public string title;   // 성향 말투 제목
        public string desc;    // 성향 말투 설명
        public string line;    // 그 길을 골랐을 때 진입 대사(성향 말투)
        public string tone;    // taunt/impressed/concern/neutral
    }
}
