using UnityEngine;

namespace AIDungeon.Director
{
    /// <summary>
    /// AI Director의 성격(캐릭터). 말투(voice)는 시스템 프롬프트에 주입돼 매 호출 목소리를 통일하고,
    /// 폴백/인트로 대사도 이 목소리로 나온다. (표정 초상은 추후 tone별로 스왑 예정)
    /// </summary>
    public class DirectorPersona
    {
        public readonly string id;
        public readonly string name;
        public readonly string voice; // 시스템 프롬프트 조각(말투 지시)

        private readonly string[] _taunt, _impressed, _concern, _neutral, _intro;

        public DirectorPersona(string id, string name, string voice,
            string[] taunt, string[] impressed, string[] concern, string[] neutral, string[] intro)
        {
            this.id = id; this.name = name; this.voice = voice;
            _taunt = taunt; _impressed = impressed; _concern = concern; _neutral = neutral; _intro = intro;
        }

        public string Fallback(string tone)
        {
            var arr = tone switch
            {
                Tone.Taunt => _taunt,
                Tone.Impressed => _impressed,
                Tone.Concern => _concern,
                _ => _neutral,
            };
            return arr[Random.Range(0, arr.Length)];
        }

        public string Intro() => _intro[Random.Range(0, _intro.Length)];
    }

    /// <summary>3인 디렉터 등록소. 런마다 하나가 배정된다.</summary>
    public static class DirectorPersonas
    {
        public static readonly DirectorPersona Aristocrat = new(
            "aristocrat", "오만한 귀족",
            "성격: 오만한 귀족. 항상 우아하고 정중하지만 상대를 깔보는 존댓말(~하시는군요, ~드리죠)로만 말한다. 냉소적이고 여유롭다.",
            taunt: new[] { "칼잡이시군요. 거리를 벌려드리죠.", "그 검이 닿기나 할지 궁금하군요." },
            impressed: new[] { "...제 예상을 벗어나시는군요.", "제법이군요. 판을 다시 짜야겠습니다." },
            concern: new[] { "지쳐 보이는군요. 잠시 숨을 고르시죠." },
            neutral: new[] { "흥미롭군요. 좀 더 지켜보죠." },
            intro: new[] { "어서 오십시오. 당신의 춤을 감상하죠." });

        public static readonly DirectorPersona Jester = new(
            "jester", "광기의 어릿광대",
            "성격: 광기의 어릿광대. 들뜨고 장난스러운 반말로, 노래하듯 통통 튀게 말한다(히히, ~할까~?). 예측 불가하고 짓궂다.",
            taunt: new[] { "히히, 이 길은 함정일까 아닐까~?", "자, 어디로 도망칠 거야~?" },
            impressed: new[] { "오~ 제법인데? 시시해지면 곤란한데!" },
            concern: new[] { "어라, 벌써 지쳤어? 재미없게~" },
            neutral: new[] { "자, 다음 장난은 뭘로 할까?" },
            intro: new[] { "히히히! 새 장난감이다! 신나게 놀아보자~" });

        public static readonly DirectorPersona Executioner = new(
            "executioner", "처형자",
            "성격: 처형자. 짧고 무겁고 위압적인 명령조·고어체로 말한다. 감정을 아끼며 으르렁댄다. 문장이 짧다.",
            taunt: new[] { "약하다. 짓밟아주마.", "도망은 없다." },
            impressed: new[] { "...제법 버티는군." },
            concern: new[] { "겨우 그 정도인가. 지루하다." },
            neutral: new[] { "다음 제물을 골라라." },
            intro: new[] { "들어와라. 무덤은 준비됐다." });

        private static readonly DirectorPersona[] All = { Aristocrat, Jester, Executioner };

        public static DirectorPersona Random() => All[UnityEngine.Random.Range(0, All.Length)];
    }
}
