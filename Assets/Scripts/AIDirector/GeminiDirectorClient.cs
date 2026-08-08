using System;
using System.Collections;
using System.Collections.Generic;
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
            "절대 규칙: 방·지형·위치·공간은 일절 묘사하지 마라. 오직 적의 전술(composition)과 플레이어 상태에만 집중하라. " +
            "tone - taunt:약점을 파고들며 도발, impressed:플레이어가 잘해 감탄, concern:플레이어가 고전해 자비, neutral:관찰. " +
            "avgHpPct가 낮으면 concern, 높으면 impressed 성향. taunt는 유저 메시지가 명시적으로 허용할 때만 쓴다.";

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
                                           DirectorPersona persona, Action<DirectorDecision> onResult,
                                           float diffOverride = float.NaN)
        {
            // 1) 전술을 코드가 먼저 확정 (직전 층과 안 겹치게 변화 보장) → 프롬프트에 실어줌
            // diffOverride: 갈림길 선택지별 난이도 사전 반영(예: 정예 전투는 이미 상향된 값으로 요청).
            string comp = DirectorPolicy.CompositionAvoiding(profile, lastComp);
            string topo = DirectorPolicy.ChooseTopologyAvoiding(profile, floor, lastTopo);
            float diff = Mathf.Clamp(
                float.IsNaN(diffOverride) ? DirectorPolicy.CanonicalDifficulty(profile) : diffOverride,
                0.8f, 1.6f);
            bool elite = DirectorPolicy.WillSpawnElite(floor, diff); // 정예 등장 여부(≈3층부터 또는 고난이도)

            string url = proxyUrl;
            if (!string.IsNullOrEmpty(modelOverride))
                url += (url.Contains("?") ? "&" : "?") + "model=" + UnityWebRequest.EscapeURL(modelOverride);

            byte[] payload = Encoding.UTF8.GetBytes(BuildRequestBody(profile, comp, diff, persona.voice, elite));

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
                    string tone = DirectorPolicy.CanonicalTone(profile);
                    if (!elite && tone == Tone.Taunt) tone = DirectorPolicy.NonTauntTone(profile); // 도발은 정예 상황만
                    decision = new DirectorDecision
                    {
                        composition = comp,
                        topology = topo,
                        difficultyModifier = diff,
                        tone = tone,
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

                    // 도발(taunt)은 정예급 난이도에서만. 그 외엔 상황 톤으로 바꾸고 대사도 교체.
                    if (!elite && decision.tone == Tone.Taunt)
                    {
                        decision.tone = DirectorPolicy.NonTauntTone(profile);
                        decision.analysis = persona.Fallback(decision.tone);
                    }

                    // 대사는 공간/지형을 일절 언급하지 않는 정책. 방 형태 단어가 새어 나오면
                    // (LLM이 지시를 어긴 것) 같은 톤 페르소나 대사로 조용히 교체.
                    if (DirectorPolicy.MentionsSpace(decision.analysis))
                    {
                        Debug.LogWarning($"[AIDirector] 공간어 언급 감지 → 페르소나 대사로 교체. \"{decision.analysis}\"");
                        decision.analysis = persona.Fallback(decision.tone);
                    }
                }

                onResult?.Invoke(decision);
            }
        }

        // === 갈림길 설계(AI가 유형 풀에서 상황·성향에 맞게 2~3개 선택 + 성향 말투 저작) ===

        private const string ForkSystemInstruction =
            "당신은 탑다운 2D 로그라이크의 AI 던전 디렉터입니다. 스테이지 클리어 후 플레이어에게 제시할 " +
            "갈림길 선택지를 설계하세요. 주어진 '유형 목록' 중 현재 플레이어 상태와 당신의 성격에 어울리는 " +
            "2~3개를 고르고, 각 선택지의 title(짧은 이름), desc(한 줄 설명), line(플레이어가 그 길을 고른 순간 " +
            "던질 당신의 한마디), tone을 모두 당신 말투로 작성합니다. " +
            "규칙: id는 반드시 유형 목록의 것만. 같은 id 중복 금지. 방·지형·위치·공간은 일절 언급 금지. " +
            "성격을 선택에 반영하라(예: 잔혹하면 고난이도를, 짓궂으면 예측불가한 조합을, 우아하면 균형 잡힌 구성을). " +
            "title/desc/line은 각각 짧고 간결하게(title은 8자 이내, line은 공백 포함 40자 이내). " +
            "특수문자·이모지·말줄임표(…)·따옴표 사용 금지, 한글과 기본 문장부호만 사용.";

        private static string ForkResponseSchema()
        {
            return
                "{\"type\":\"OBJECT\",\"properties\":{\"options\":{\"type\":\"ARRAY\",\"minItems\":2,\"maxItems\":3," +
                "\"items\":{\"type\":\"OBJECT\",\"properties\":{" +
                "\"id\":{\"type\":\"STRING\",\"enum\":[" + ForkArchetypes.IdEnumJson() + "]}," +
                "\"title\":{\"type\":\"STRING\"},\"desc\":{\"type\":\"STRING\"},\"line\":{\"type\":\"STRING\"}," +
                "\"tone\":{\"type\":\"STRING\",\"enum\":[\"taunt\",\"impressed\",\"concern\",\"neutral\"]}}," +
                "\"required\":[\"id\",\"title\",\"desc\",\"line\",\"tone\"]," +
                "\"propertyOrdering\":[\"id\",\"title\",\"desc\",\"line\",\"tone\"]}}}," +
                "\"required\":[\"options\"]}";
        }

        /// <summary>
        /// 갈림길 선택지를 AI에게 설계 요청. 실패/무효 시 항상 코드 기본 3종으로 콜백(게임 안 멈춤).
        /// 수치(난이도/적수/회복)는 id에 매핑된 ForkArchetype이 소유 → AI는 선택+연출만.
        /// </summary>
        public IEnumerator RequestForkOptions(PlayerProfile profile, DirectorPersona persona,
                                              Action<List<ForkChoice>> onResult)
        {
            string url = proxyUrl;
            if (!string.IsNullOrEmpty(modelOverride))
                url += (url.Contains("?") ? "&" : "?") + "model=" + UnityWebRequest.EscapeURL(modelOverride);

            byte[] payload = Encoding.UTF8.GetBytes(BuildForkRequestBody(profile, persona.voice));

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(timeoutSeconds);

                yield return req.SendWebRequest();

                List<ForkChoice> choices = null;
                if (req.result == UnityWebRequest.Result.Success)
                    choices = ParseForkChoices(req.downloadHandler.text, profile, persona);
                else
                    Debug.LogWarning($"[AIDirector] 갈림길 요청 실패({req.result}) → 기본 선택지. {req.error}");

                onResult?.Invoke(choices ?? ForkArchetypes.DefaultChoices());
            }
        }

        [Serializable] private class ForkDto { public string id, title, desc, line, tone; }
        [Serializable] private class ForkListDto { public ForkDto[] options; }

        private List<ForkChoice> ParseForkChoices(string json, PlayerProfile profile, DirectorPersona persona)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            ForkListDto dto;
            try { dto = JsonUtility.FromJson<ForkListDto>(json); }
            catch (Exception e) { Debug.LogWarning($"[AIDirector] 갈림길 파싱 예외: {e.Message}"); return null; }
            if (dto?.options == null || dto.options.Length < 2) return null;

            var seen = new HashSet<string>();
            var list = new List<ForkChoice>(3);
            foreach (var o in dto.options)
            {
                if (o == null || !ForkArchetypes.IsValidId(o.id) || !seen.Add(o.id)) continue; // 무효/중복 id 제거
                string tone = DirectorPolicy.IsValidTone(o.tone) ? o.tone : Tone.Neutral;
                // 갈림길 대사도 공간 언급 금지 정책 적용(어기면 성향 대사로 교체).
                string line = string.IsNullOrWhiteSpace(o.line) || DirectorPolicy.MentionsSpace(o.line)
                    ? persona.Fallback(tone) : o.line.Trim();
                var arch = ForkArchetypes.ById(o.id);
                list.Add(new ForkChoice
                {
                    id = o.id,
                    title = string.IsNullOrWhiteSpace(o.title) ? arch.title : o.title.Trim(),
                    desc = string.IsNullOrWhiteSpace(o.desc) ? arch.desc : o.desc.Trim(),
                    line = line,
                    tone = tone,
                });
                if (list.Count >= 3) break;
            }
            return list.Count >= 2 ? list : null;
        }

        private string BuildForkRequestBody(PlayerProfile p, string voice)
        {
            string userText =
                $"{p.ToPromptLine()} | 유형 목록: {ForkArchetypes.Menu()} | " +
                "위 유형 중 2~3개를 골라 선택지를 설계하라.";

            var sb = new StringBuilder(1024);
            sb.Append("{\"systemInstruction\":{\"parts\":[{\"text\":");
            AppendJsonString(sb, ForkSystemInstruction + " " + voice);
            sb.Append("}]},\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":");
            AppendJsonString(sb, userText);
            sb.Append("}]}],\"generationConfig\":{\"responseMimeType\":\"application/json\",\"responseSchema\":");
            sb.Append(ForkResponseSchema());
            sb.Append(",\"temperature\":");
            sb.Append(temperature.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append("}}");
            return sb.ToString();
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

        private string BuildRequestBody(PlayerProfile p, string comp, float diff, string voice, bool elite)
        {
            var c = CultureInfo.InvariantCulture;
            // topology는 일부러 넘기지 않는다(대사가 방을 언급할 근거 자체를 제거).
            string userText =
                $"{p.ToPromptLine()} | 적 구성: composition={comp}, " +
                string.Format(c, "difficultyModifier={0:0.00}", diff) +
                " | 대사 지침: " + CompositionBrief(comp) +
                (elite
                    ? " | 이번엔 정예(챔피언)급 강적이 등장한다. 대사에 이 강적의 등장을 반드시 언급하라. tone은 taunt(도발) 허용."
                    : " | 평범한 난이도다. tone에 taunt(도발)를 쓰지 마라. impressed/concern/neutral 중에서만 골라라.");

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
