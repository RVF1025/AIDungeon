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
            "(2) tone: 아래 넷 중 하나. " +
            "composition 의미 - kiter_pack:원거리 적들이 거리를 유지하며 근접 플레이어의 공격이 자기들에게 '닿지 못하게' 함(플레이어가 못 닿는다는 방향으로 서술), " +
            "rusher_pack:빠른근접으로 원거리플레이어 압박('순식간에 접근'), tank_bait:탱커 미끼로 저돌형 유인('벽/미끼'), balanced:균형. " +
            "절대 규칙: 공간(방 형태)은 유저 메시지의 '이번 공간' 설명에 적힌 것만 묘사하라. " +
            "거기에 없는 다른 방 형태는 한 단어도 언급하지 마라(특히 '이번 공간'이 엄폐물이 아니면 '엄폐물'이라는 단어를 절대 쓰지 마라). " +
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
            string[] foreign;
            switch (topo)
            {
                case Topology.Corridor: foreign = new[] { "엄폐", "개활", "포위", "사방", "탁 트" }; break;
                case Topology.Encircle: foreign = new[] { "엄폐", "복도", "통로", "개활", "탁 트" }; break;
                case Topology.Cover:    foreign = new[] { "복도", "통로", "개활", "포위", "사방", "탁 트" }; break;
                case Topology.Open:     foreign = new[] { "엄폐", "복도", "통로", "포위", "사방" }; break;
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
                case Topology.Encircle: return "사방에서 에워싸 도망칠 코너가 없다(넓은 방, 등 뒤에서도 튀어나옴). '사방/포위/퇴로 없음'으로만 묘사.";
                case Topology.Cover:    return "기둥과 엄폐물이 사선을 끊는다. '엄폐물/기둥/시야 차단'으로만 묘사.";
                case Topology.Open:     return "탁 트인 개활지, 숨을 곳이 없다. '개활지/탁 트임/노출'로만 묘사.";
                case Topology.Corridor: return "좁은 통로가 1:1을 강요한다. '좁은 통로/일렬/한 명씩'으로만 묘사.";
                default:                return "평범한 방.";
            }
        }

        private string BuildRequestBody(PlayerProfile p, string comp, string topo, float diff, string voice)
        {
            var c = CultureInfo.InvariantCulture;
            string userText =
                $"{p.ToPromptLine()} | 결정된 전술: composition={comp}, topology={topo}, " +
                string.Format(c, "difficultyModifier={0:0.00}", diff) +
                " | 이번 공간: " + TopologyBrief(topo);

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
