using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace AIDungeon.Director
{
    /// <summary>
    /// Cloudflare Worker 프록시를 통해 Gemini에 층 전환 판단을 요청한다.
    /// WebGL 호환(코루틴 + UnityWebRequest, 스레드 없음).
    ///
    /// 파이프라인:
    ///   1) DirectorPolicy가 전술(composition/topology/difficulty)을 결정론적으로 계산
    ///   2) 그 전술을 프롬프트에 실어 AI에게 대사(analysis)+태도(tone)만 요청 (빠르고 일치 보장)
    ///   3) 실패/타임아웃 → FallbackPresets로 즉시 대체 (게임 절대 안 멈춤)
    /// </summary>
    public class GeminiDirectorClient : MonoBehaviour
    {
        [Header("프록시 설정")]
        [Tooltip("Vercel 프록시 엔드포인트. 프로덕션 고정 주소 사용.")]
        public string proxyUrl = "https://ai-dungeon-nine.vercel.app/api/gemini";

        [Tooltip("3.5-flash-lite: ~1초, 무료 한도 여유(측정). flash-latest는 느리고 429 잦음.")]
        public string modelOverride = "gemini-3.5-flash-lite";

        [Header("동작")]
        [Tooltip("초과 시 폴백. 대사는 백그라운드로 받으니 넉넉해도 게임 안 멈춤.")]
        public float timeoutSeconds = 12f;

        [Range(0f, 1f)] public float temperature = 0.6f;

        [Tooltip("true면 전술 결정도 AI에게 맡기고 코드는 검증만 함(신뢰성 ↓). 기본 false 권장.")]
        public bool letAiDecideTactics = false;

        // AI에게는 대사+태도만 요청(전술은 코드가 이미 결정해 프롬프트로 전달).
        private const string SystemInstruction =
            "당신은 탑다운 2D 로그라이크의 AI 던전 디렉터 캐릭터입니다. 다음 스테이지의 전술은 이미 결정되어 주어집니다. " +
            "당신의 일은 두 가지: (1) analysis: 그 전술을 은근히 암시하는, 플레이어를 향한 캐릭터성 있는 한국어 한 문장. " +
            "반드시 짧고 간결하게(공백 포함 40자 이내, 한 문장). 미사여구·수식어를 늘어놓지 마라. " +
            "(2) tone: 아래 넷 중 하나. " +
            "composition 의미 - kiter_pack:원거리 적들이 거리를 유지하며 근접 플레이어의 공격이 자기들에게 '닿지 못하게' 함(플레이어가 못 닿는다는 방향으로 서술), " +
            "rusher_pack:빠른근접으로 원거리플레이어 압박('순식간에 접근'), tank_bait:탱커 미끼로 저돌형 유인('벽/미끼'), balanced:균형. " +
            "절대 규칙: 방의 형태·지형은 유저 메시지의 '이번 공간' 키워드에 맞춰라. 거기 없는 다른 지형은 만들어내지 마라. " +
            "단, '이번 공간' 설명 문구를 그대로 베끼지 말고 캐릭터 말투로 짧게 재해석하라. " +
            "tone - taunt:약점을 파고들며 도발, impressed:플레이어가 잘해 감탄, concern:플레이어가 고전해 자비, neutral:관찰. " +
            "avgHpPct가 낮으면 concern, 높으면 impressed 또는 taunt 성향.";

        // 대사+태도만 받는 축소 스키마 → 토큰↓ 지연↓.
        private const string ResponseSchemaNarration =
            "{\"type\":\"OBJECT\",\"properties\":{" +
            "\"analysis\":{\"type\":\"STRING\"}," +
            "\"tone\":{\"type\":\"STRING\",\"enum\":[\"taunt\",\"impressed\",\"concern\",\"neutral\"]}}," +
            "\"required\":[\"analysis\",\"tone\"],\"propertyOrdering\":[\"analysis\",\"tone\"]}";

        /// <summary>
        /// 층 전환 판단을 비동기로 요청. 성공/실패 무관하게 항상 onResult가 호출된다.
        /// 사용: StartCoroutine(client.RequestDecision(profile, d => { ... }));
        /// </summary>
        public IEnumerator RequestDecision(PlayerProfile profile, int floor, string lastComp, string lastTopo,
                                           DirectorPersona persona, Action<DirectorDecision> onResult)
        {
            // 1) 전술을 코드가 먼저 확정 (직전 층과 안 겹치게 변화 보장) → 프롬프트에 실어줌
            string comp = DirectorPolicy.CompositionAvoiding(profile, lastComp);
            string topo = DirectorPolicy.ChooseTopologyAvoiding(profile, floor, lastTopo);
            float diff = DirectorPolicy.CanonicalDifficulty(profile);

            string url = proxyUrl;
            if (!string.IsNullOrEmpty(modelOverride))
                url += (url.Contains("?") ? "&" : "?") + "model=" + UnityWebRequest.EscapeURL(modelOverride);

            byte[] payload = Encoding.UTF8.GetBytes(BuildRequestBody(profile, comp, topo, diff, persona.voice));

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(timeoutSeconds);

                yield return req.SendWebRequest();

                DirectorDecision decision = null;

                if (req.result == UnityWebRequest.Result.Success)
                {
                    decision = TryParse(req.downloadHandler.text);
                    if (decision == null)
                        Debug.LogWarning($"[AIDirector] 응답 파싱 실패 → 폴백. raw: {req.downloadHandler.text}");
                }
                else
                {
                    Debug.LogWarning($"[AIDirector] 요청 실패({req.result}) → 폴백. {req.error}");
                }

                if (decision == null)
                {
                    // 폴백도 페르소나 목소리로(전술은 이미 계산한 comp/topo/diff 사용)
                    decision = new DirectorDecision
                    {
                        composition = comp,
                        topology = topo,
                        difficultyModifier = diff,
                        tone = DirectorPolicy.CanonicalTone(profile),
                        fromFallback = true,
                    };
                    decision.analysis = persona.Fallback(decision.tone);
                }
                else
                {
                    // AI가 준 analysis/tone 유지, 전술은 코드 규범으로 채움/검증
                    decision.composition = comp;
                    decision.topology = topo;
                    decision.difficultyModifier = diff;
                    DirectorPolicy.Reconcile(decision, profile, letAiDecideTactics);

                    // 공간 묘사 모순 방어: LLM이 프롬프트를 어기고 topology와 안 맞는 공간어를
                    // 넣으면(예: corridor인데 '엄폐물') 같은 톤 페르소나 대사로 조용히 교체.
                    if (MentionsForeignSpace(decision.analysis, decision.topology))
                    {
                        Debug.LogWarning($"[AIDirector] topology({decision.topology}) 불일치 공간어 → 페르소나 대사로 교체. \"{decision.analysis}\"");
                        decision.analysis = persona.Fallback(decision.tone);
                    }
                }

                onResult?.Invoke(decision);
            }
        }

        // 주어진 topology에 속하지 않는 '남의 방' 공간어가 대사에 있으면 true(모순).
        private static bool MentionsForeignSpace(string analysis, string topo)
        {
            if (string.IsNullOrEmpty(analysis)) return false;
            // '사방'은 포위·탁트임 양쪽에 쓰여 애매하므로 제외(포위/에워/둘러싸로 판별).
            // encircle은 '넓은 방'이라 개활지/탁 트임 언급이 모순 아님 → 금지 안 함.
            // 동의어까지 차단: cover계열=엄폐/기둥/차폐/은폐, open계열=개활/광야/벌판/탁 트,
            //                narrow계열=복도/통로/일렬/틈새, surround계열=포위/에워/둘러싸.
            string[] foreign;
            switch (topo)
            {
                case Topology.Corridor: foreign = new[] { "엄폐", "기둥", "차폐", "은폐", "개활", "광야", "벌판", "탁 트", "포위", "에워", "둘러싸" }; break;
                case Topology.Encircle: foreign = new[] { "엄폐", "기둥", "차폐", "은폐", "복도", "통로", "일렬", "틈새" }; break;
                case Topology.Cover:    foreign = new[] { "복도", "통로", "개활", "광야", "벌판", "탁 트", "포위", "에워", "둘러싸" }; break;
                case Topology.Open:     foreign = new[] { "엄폐", "기둥", "차폐", "은폐", "복도", "통로", "일렬", "틈새" }; break;
                default: return false;
            }
            foreach (var w in foreign)
                if (analysis.Contains(w)) return true;
            return false;
        }

        // 이번 요청의 topology '한 개'만 유저 메시지에 실어준다(시스템 프롬프트엔 다른 방 형태 단어가
        // 없으므로 모델이 '엄폐물' 같은 남의 방 단어를 복사할 여지 자체를 제거).
        private static string TopologyBrief(string topo)
        {
            switch (topo)
            {
                // 짧은 키워드만. (모델이 문장을 통째로 베끼지 못하도록 서술문이 아닌 태그로 제공)
                case Topology.Encircle: return "포위(넓은 방, 사방이 적, 퇴로 없음)";
                case Topology.Cover:    return "엄폐(기둥·차폐물로 시야/사선 차단)";
                case Topology.Open:     return "개활지(탁 트임, 숨을 곳 없음)";
                case Topology.Corridor: return "좁은 통로(일렬, 1:1)";
                default:                return "일반 방";
            }
        }

        // composition별 대사 초점. 특히 balanced는 특정 전술 우위(거리/접근/미끼) 주장을 금지하고
        // 플레이어의 '지금 상황'(체력/난이도)만 근거로 삼게 해 모순·어색함을 막는다.
        private static string CompositionBrief(string comp)
        {
            switch (comp)
            {
                case Composition.KiterPack:  return "방 형태는 언급 말고 오직 '거리/사거리/네 공격이 닿지 못함'에만 집중.";
                case Composition.RusherPack: return "'순식간에 접근/거리를 좁혀 압박'에 집중.";
                case Composition.TankBait:   return "'미끼/유인/함정'에 집중.";
                default:                     return "이번엔 특별한 전술이 없다. 공간이나 전투 방식은 일절 언급하지 말고, 오직 플레이어의 현재 체력 상태만 근거로 한 문장 도발/관찰하라. 체력이 높으면 여유를 비웃고, 낮으면 곧 무너질 것을 조롱하라.";
            }
        }

        private string BuildRequestBody(PlayerProfile p, string comp, string topo, float diff, string voice)
        {
            var c = CultureInfo.InvariantCulture;
            string userText =
                $"{p.ToPromptLine()} | 결정된 전술: composition={comp}, topology={topo}, " +
                string.Format(c, "difficultyModifier={0:0.00}", diff) +
                " | 이번 공간: " + TopologyBrief(topo) +
                " | 대사 지침: " + CompositionBrief(comp);

            var sb = new StringBuilder(1024);
            sb.Append("{\"systemInstruction\":{\"parts\":[{\"text\":");
            AppendJsonString(sb, SystemInstruction + " " + voice);
            sb.Append("}]},\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":");
            AppendJsonString(sb, userText);
            sb.Append("}]}],\"generationConfig\":{\"responseMimeType\":\"application/json\",\"responseSchema\":");
            sb.Append(ResponseSchemaNarration);
            sb.Append(",\"temperature\":");
            sb.Append(temperature.ToString("0.0", c));
            sb.Append("}}");
            return sb.ToString();
        }

        // 프록시가 Director JSON(analysis/tone)만 그대로 돌려주므로 한 방에 파싱.
        private DirectorDecision TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var d = JsonUtility.FromJson<DirectorDecision>(json);
                if (d == null || string.IsNullOrWhiteSpace(d.analysis)) return null;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIDirector] JSON 예외: {e.Message}");
                return null;
            }
        }

        // JsonUtility엔 문자열 이스케이프 유틸이 없어 최소 구현.
        private static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            sb.Append('"');
        }
    }
}
