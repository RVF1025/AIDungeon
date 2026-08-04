// AI Dungeon - Gemini proxy (Vercel Serverless Function, Node 18+ 런타임)
// Cloudflare Workers는 egress 지역이 뽑기라 Gemini가 "User location not supported"를 자주 뱉는다.
// Vercel Hobby 함수는 기본 리전(iad1, 미국 동부)에 고정 실행 → 위치 에러 없음.
//
// 배포: Vercel에 이 GitHub 리포 import, Root Directory = "proxy", Framework = Other.
// 환경변수: GEMINI_API_KEY (Vercel > Project > Settings > Environment Variables).
// 엔드포인트: https://<project>.vercel.app/api/gemini  (Unity의 proxyUrl에 이 주소)

export default async function handler(req, res) {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");

  if (req.method === "OPTIONS") return res.status(204).end();
  if (req.method !== "POST") return res.status(405).json({ error: "POST only" });

  const key = process.env.GEMINI_API_KEY;
  if (!key) return res.status(500).json({ error: "server missing GEMINI_API_KEY" });

  const model =
    (req.query && req.query.model) || process.env.MODEL || "gemini-flash-lite-latest";

  // Vercel은 JSON 본문을 자동 파싱한다. 원문 그대로 Gemini에 넘기려 재직렬화.
  const body = typeof req.body === "string" ? req.body : JSON.stringify(req.body || {});

  const endpoint =
    `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`;

  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), 7000); // 7초 초과 → Unity 폴백 (대사는 비동기라 UX 지장 X)

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
    return res.status(502).json({ error: "upstream timeout/fail", detail: String(e) });
  }
  clearTimeout(timer);

  if (!g.ok) {
    const detail = await g.text();
    return res.status(502).json({ error: "gemini error", status: g.status, detail });
  }

  const data = await g.json();
  const text = data?.candidates?.[0]?.content?.parts?.[0]?.text;
  if (!text) return res.status(502).json({ error: "no text in gemini response" });

  // Director JSON(문자열) 그대로 반환 → Unity가 JsonUtility 한 방에 파싱.
  res.setHeader("Content-Type", "application/json");
  return res.status(200).send(text);
}
