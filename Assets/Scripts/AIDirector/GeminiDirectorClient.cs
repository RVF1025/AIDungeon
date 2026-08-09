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

        // === 갈림길 평가(AI가 제시된 선택지들을 성향으로 한 문장 논평) ===

        private const string ForkSystemInstruction =
            "당신은 탑다운 2D 로그라이크의 AI 던전 디렉터입니다. 스테이지 클리어 후, 플레이어에게 제시된 " +
            "갈림길 선택지 목록을 보고 이 갈림길을 당신 성격으로 한 문장 평가/논평하세요(선택을 유도하듯). " +
            "'직전 전투 요약'을 폭넓게 반영하라: 전투 스타일(근접/원거리 편중이나 균형), 공격성, 체력 소모 중 " +
            "가장 두드러진 점을 골라 언급. 체력만 반복하지 말 것. 한 가지 공격만 치우쳤으면 그 점을 꼬집어도 좋다. " +
            "'???' 선택지가 목록에 있으면 그 정체에 대한 호기심을 자극하는 힌트를 살짝 흘려도 좋다. " +
            "단, 유저 메시지에 '최근 사건'이 명시되면 직전 전투 평가는 생략하고, 그 사건에 반응하며 " +
            "이 갈림길에서 무엇을 고르면 좋을지 조언·추천을 해라. " +
            "방·지형·위치·공간은 일절 언급 금지. line: 공백 포함 40자 이내 한 문장. " +
            "tone: taunt/impressed/concern/neutral 중 하나. 특수문자·이모지·말줄임표(…) 금지, 한글과 기본 문장부호만.";

        private const string ForkResponseSchema =
            "{\"type\":\"OBJECT\",\"properties\":{" +
            "\"line\":{\"type\":\"STRING\"}," +
            "\"tone\":{\"type\":\"STRING\",\"enum\":[\"taunt\",\"impressed\",\"concern\",\"neutral\"]}}," +
            "\"required\":[\"line\",\"tone\"],\"propertyOrdering\":[\"line\",\"tone\"]}";

        /// <summary>
        /// 제시된 갈림길 선택지들을 AI가 한 문장으로 평가. 실패 시 성향 로컬 대사로 폴백(게임 안 멈춤).
        /// </summary>
        public IEnumerator RequestForkComment(PlayerProfile profile, DirectorPersona persona,
                                              List<ForkArchetype> options, string recentEvent, Action<ForkComment> onResult)
        {
            string url = proxyUrl;
            if (!string.IsNullOrEmpty(modelOverride))
                url += (url.Contains("?") ? "&" : "?") + "model=" + UnityWebRequest.EscapeURL(modelOverride);

            byte[] payload = Encoding.UTF8.GetBytes(BuildForkRequestBody(profile, persona.voice, options, recentEvent));

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(payload);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(timeoutSeconds);

                yield return req.SendWebRequest();

                ForkComment comment = null;
                if (req.result == UnityWebRequest.Result.Success)
                    comment = ParseForkComment(req.downloadHandler.text, persona);
                else
                    Debug.LogWarning($"[AIDirector] 갈림길 평가 실패({req.result}) → 폴백. {req.error}");

                if (comment == null)
                {
                    string tone = DirectorPolicy.NonTauntTone(profile);
                    comment = new ForkComment { tone = tone, line = persona.Fork() };
                }
                onResult?.Invoke(comment);
            }
        }

        private const string SituationSystemInstruction =
            "당신은 탑다운 2D 로그라이크의 AI 던전 디렉터입니다. 주어진 상황을 당신 성격으로 한 문장 논평/평가하세요 " +
            "(플레이어의 현재 상태를 반영한 소감). 방·지형·위치·공간은 일절 언급 금지. " +
            "line: 공백 포함 40자 이내. tone: taunt/impressed/concern/neutral 중 하나. 특수문자·이모지 금지.";

        /// <summary>임의의 상황(situation)에 대한 AI 소감 한 문장. 실패 시 성향 폴백 대사.</summary>
        public IEnumerator RequestSituationComment(PlayerProfile profile, DirectorPersona persona,
                                                   string situation, Action<ForkComment> onResult)
        {
            string url = proxyUrl;
            if (!string.IsNullOrEmpty(modelOverride))
                url += (url.Contains("?") ? "&" : "?") + "model=" + UnityWebRequest.EscapeURL(modelOverride);

            var sb = new StringBuilder(1024);
            sb.Append("{\"systemInstruction\":{\"parts\":[{\"text\":");
            AppendJsonString(sb, SituationSystemInstruction + " " + persona.voice);
            sb.Append("}]},\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":");
            AppendJsonString(sb, $"{profile.ToPromptLine()} | 상황: {situation}");
            sb.Append("}]}],\"generationConfig\":{\"responseMimeType\":\"application/json\",\"responseSchema\":");
            sb.Append(ForkResponseSchema);
            sb.Append(",\"temperature\":");
            sb.Append(temperature.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append("}}");

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(sb.ToString()));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(timeoutSeconds);

                yield return req.SendWebRequest();

                ForkComment comment = null;
                if (req.result == UnityWebRequest.Result.Success)
                    comment = ParseForkComment(req.downloadHandler.text, persona);
                else
                    Debug.LogWarning($"[AIDirector] 상황 평가 실패({req.result}) → 폴백. {req.error}");

                if (comment == null)
                {
                    string tone = DirectorPolicy.NonTauntTone(profile);
                    comment = new ForkComment { tone = tone, line = persona.Fallback(tone) };
                }
                onResult?.Invoke(comment);
            }
        }

        [Serializable] private class ForkCommentDto { public string line, tone; }

        private ForkComment ParseForkComment(string json, DirectorPersona persona)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            ForkCommentDto dto;
            try { dto = JsonUtility.FromJson<ForkCommentDto>(json); }
            catch (Exception e) { Debug.LogWarning($"[AIDirector] 갈림길 평가 파싱 예외: {e.Message}"); return null; }
            if (dto == null) return null;

            string tone = DirectorPolicy.IsValidTone(dto.tone) ? dto.tone : Tone.Neutral;
            string line = string.IsNullOrWhiteSpace(dto.line) || DirectorPolicy.MentionsSpace(dto.line)
                ? persona.Fork() : dto.line.Trim();
            return new ForkComment { line = line, tone = tone };
        }

        // 수치를 사람이 읽을 서술로 변환(모델이 다양한 각도로 논평하도록). meleeRatio 1=근접만/0=원거리만.
        private static string CombatSummary(PlayerProfile p)
        {
            string hp =
                p.avgHpPct <= 0.4f ? "체력을 크게 소모함(위태로움)" :
                p.avgHpPct <= 0.7f ? "체력을 제법 소모함" : "체력에 여유가 있음";
            string aggr =
                p.aggression >= 0.65f ? "매우 저돌적" :
                p.aggression <= 0.35f ? "신중하게 거리 유지" : "공격성 보통";
            // 근접/원거리가 균형이면 스타일은 언급 가치가 낮으니 생략(체력·성향에 집중).
            string style =
                p.meleeRatio >= 0.75f ? "근접에 극단적으로 치우침" :
                p.meleeRatio >= 0.58f ? "근접 위주" :
                p.meleeRatio <= 0.25f ? "원거리에 극단적으로 치우침" :
                p.meleeRatio <= 0.42f ? "원거리 위주" : null;
            return style == null ? $"{hp}, 성향={aggr}" : $"스타일={style}, {hp}, 성향={aggr}";
        }

        private string BuildForkRequestBody(PlayerProfile p, string voice, List<ForkArchetype> options, string recentEvent)
        {
            var titles = new StringBuilder();
            for (int i = 0; i < options.Count; i++)
            {
                if (i > 0) titles.Append(", ");
                titles.Append(options[i].title);
            }
            string context = string.IsNullOrEmpty(recentEvent)
                ? $"직전 전투 요약: {CombatSummary(p)}"
                : $"최근 사건: {recentEvent}";
            string userText = $"{p.ToPromptLine()} | {context} | 제시된 갈림길: {titles} | 이 갈림길을 평가하라.";

            var sb = new StringBuilder(1024);
            sb.Append("{\"systemInstruction\":{\"parts\":[{\"text\":");
            AppendJsonString(sb, ForkSystemInstruction + " " + voice);
            sb.Append("}]},\"contents\":[{\"role\":\"user\",\"parts\":[{\"text\":");
            AppendJsonString(sb, userText);
            sb.Append("}]}],\"generationConfig\":{\"responseMimeType\":\"application/json\",\"responseSchema\":");
            sb.Append(ForkResponseSchema);
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
