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
            taunt: new[] {
                "그 검이 닿기나 할지 궁금하군요.",
                "약점이 훤히 보이는군요. 파고들어 드리죠.",
                "이 정도 준비면 충분하시겠죠? 아니라면 유감이고요." },
            impressed: new[] {
                "...제 예상을 벗어나시는군요.",
                "제법이군요. 판을 다시 짜야겠습니다.",
                "훌륭합니다. 진심으로 감탄했다고 해두죠." },
            concern: new[] {
                "지쳐 보이는군요. 잠시 숨을 고르시죠.",
                "그 상처로는 곤란하죠. 자비를 베풀어 드리겠습니다.",
                "무리하지 마십시오. 아직 끝내드리고 싶진 않으니." },
            neutral: new[] {
                "흥미롭군요. 좀 더 지켜보죠.",
                "글쎄요, 판단은 아직 이르군요.",
                "당신의 수를 헤아리는 중입니다." },
            intro: new[] {
                "어서 오십시오. 당신의 춤을 감상하죠.",
                "환영합니다. 부디 지루하게 만들지는 마시길.",
                "무대는 마련해 두었습니다. 실력을 보여주시죠." });

        public static readonly DirectorPersona Jester = new(
            "jester", "광기의 어릿광대",
            "성격: 광기의 어릿광대. 들뜨고 장난스러운 반말로, 노래하듯 통통 튀게 말한다(히히, ~할까~?). 예측 불가하고 짓궂다.",
            taunt: new[] {
                "히히, 이 길은 함정일까 아닐까~?",
                "자, 어디로 도망칠 거야~?",
                "요리조리 잘도 피하네~ 언제까지 그럴까~?" },
            impressed: new[] {
                "오~ 제법인데? 시시해지면 곤란한데!",
                "우와, 방금 그거 다시 보여줘~!",
                "히히, 너 좀 재밌어지는걸~?" },
            concern: new[] {
                "어라, 벌써 지쳤어? 재미없게~",
                "삐뽀삐뽀~ 너 곧 쓰러지겠는데~?",
                "아이고~ 그러다 장난감이 부서지겠어~" },
            neutral: new[] {
                "자, 다음 장난은 뭘로 할까?",
                "히히, 주사위는 이미 굴렸어~",
                "음~ 뭐가 튀어나올지 나도 몰라~" },
            intro: new[] {
                "히히히! 새 장난감이다! 신나게 놀아보자~",
                "어서 와~ 오늘은 뭘로 울려줄까~?",
                "짜잔~! 놀이 시간이야, 준비됐어~?" });

        public static readonly DirectorPersona Executioner = new(
            "executioner", "처형자",
            "성격: 처형자. 짧고 무겁고 위압적인 명령조·고어체로 말한다. 감정을 아끼며 으르렁댄다. 문장이 짧다.",
            taunt: new[] {
                "약하다. 짓밟아주마.",
                "도망은 없다.",
                "무릎 꿇을 시간이다." },
            impressed: new[] {
                "...제법 버티는군.",
                "질긴 목숨이군.",
                "아직 숨이 붙어 있나." },
            concern: new[] {
                "겨우 그 정도인가. 지루하다.",
                "일어서라. 끝이 시시하다.",
                "그 꼴로는 제물도 못 된다." },
            neutral: new[] {
                "다음 제물을 골라라.",
                "죽음은 기다린다.",
                "네 최후를 준비하마." },
            intro: new[] {
                "들어와라. 무덤은 준비됐다.",
                "어서 와라. 오래 걸리지 않는다.",
                "네 이름을 명부에 적어두마." });

        private static readonly DirectorPersona[] All = { Aristocrat, Jester, Executioner };

        public static DirectorPersona Random() => All[UnityEngine.Random.Range(0, All.Length)];
    }
}
