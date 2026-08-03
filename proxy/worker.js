// AI Dungeon - Gemini proxy (Cloudflare Worker)
// 역할: WebGL 클라이언트의 CORS 우회 + API 키 서버측 주입 + 응답 언랩 + 3초 타임아웃.
// Unity는 Gemini generateContent "요청 본문"을 그대로 이 Worker에 POST한다(키 제외).
// Worker가 candidates[0].content.parts[0].text (=Director JSON)만 뽑아 돌려주므로
// Unity 파싱이 한 줄로 끝난다.
//
// 배포: Cloudflare 대시보드 > Workers & Pages > Create Worker > 이 파일 붙여넣기 > Deploy.
// 설정: Settings > Variables > 'GEMINI_API_KEY'를 Secret으로 추가(값=본인 키).
//       (선택) 'MODEL' 변수로 기본 모델 변경. 기본값 gemini-flash-lite-latest.

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
};

function json(obj, status) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });
}

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS") return new Response(null, { headers: CORS });
    if (request.method !== "POST") return json({ error: "POST only" }, 405);

    const key = env.GEMINI_API_KEY;
    if (!key) return json({ error: "server missing GEMINI_API_KEY secret" }, 500);

    const url = new URL(request.url);
    const model = url.searchParams.get("model") || env.MODEL || "gemini-flash-lite-latest";

    let body;
    try {
      body = await request.text();
    } catch {
      return json({ error: "unreadable request body" }, 400);
    }

    const endpoint =
      `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`;

    // 3초 타임아웃 → 초과 시 Unity가 폴백 프리셋을 쓰도록 502 반환.
    const ctrl = new AbortController();
    const timer = setTimeout(() => ctrl.abort(), 3000);

    let g;
    try {
      g = await fetch(endpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json", "x-goog-api-key": key },
        body,
        signal: ctrl.signal,
      });
    } catch (e) {
      clearTimeout(timer);
      return json({ error: "upstream timeout or network fail", detail: String(e) }, 502);
    }
    clearTimeout(timer);

    if (!g.ok) {
      const detail = await g.text();
      return json({ error: "gemini error", status: g.status, detail }, 502);
    }

    const data = await g.json();
    const text = data?.candidates?.[0]?.content?.parts?.[0]?.text;
    if (!text) return json({ error: "no text in gemini response", raw: data }, 502);

    // Director JSON(문자열)을 그대로 반환 → Unity에서 JsonUtility.FromJson 한 방.
    return new Response(text, {
      status: 200,
      headers: { ...CORS, "Content-Type": "application/json" },
    });
  },
};
