using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>
    /// TextMeshPro 기반 HUD + AI Director 대사창. WebGL에서 한글이 나오도록 NanumGothic을
    /// 런타임 동적 폰트로 굽는다(IMGUI 동적폰트는 WebGL에서 CJK 미표시). 설계 4장 대사창의 기초.
    /// 전제: Window > TextMeshPro > Import TMP Essential Resources 1회 필요.
    /// </summary>
    public class DirectorHud : MonoBehaviour
    {
        private TMP_FontAsset _font;
        private TextMeshProUGUI _status, _tag, _analysis;

        private void Awake()
        {
            _font = LoadKoreanFont();
            BuildUI();
        }

        private TMP_FontAsset LoadKoreanFont()
        {
            // 에디터에서 미리 구운 정적 아틀라스를 로드(런타임 래스터화 없음 → WebGL 안전).
            // 없으면 TMP 기본 폰트로 진행(한글 미표시, 크래시는 안 남).
            var baked = Resources.Load<TMP_FontAsset>("Fonts/NanumKR");
            if (baked == null)
                Debug.LogWarning("[DirectorHud] 'Resources/Fonts/NanumKR' 폰트 에셋이 없습니다. " +
                    "Font Asset Creator로 NanumGothic 정적 아틀라스를 구워 저장하세요.");
            return baked;
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("DirectorCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 좌상단 상태 (층/HP/적)
            _status = MakeText(canvas.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(30, -24), new Vector2(900, 60), 34, TextAlignmentOptions.TopLeft);

            // 하단 AI Director 대사 패널
            var panel = MakePanel(canvas.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(1500, 210),
                new Color(0.05f, 0.06f, 0.12f, 0.82f));

            _tag = MakeText(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(28, -14), new Vector2(-56, 40), 26, TextAlignmentOptions.TopLeft);
            _tag.color = new Color(0.55f, 0.8f, 1f);

            _analysis = MakeText(panel, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(28, -56), new Vector2(-56, -70), 42, TextAlignmentOptions.TopLeft);

            // AI 전용 프레임 느낌의 상단 테두리(설계 4장: 다른 UI와 구분되는 색/테두리)
            var border = MakePanel(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                Vector2.zero, new Vector2(0, 4), new Color(0.4f, 0.75f, 1f, 0.9f));
            border.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 0);

            SetStatus("");
            _tag.text = "";
            _analysis.text = "";
        }

        private RectTransform MakePanel(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        private TextMeshProUGUI MakeText(Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 sizeDelta, float size, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = sizeDelta;
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        public void SetStatus(string s) { if (_status != null) _status.text = s; }

        public void ShowDecision(DirectorDecision d)
        {
            if (d == null) return;
            _tag.text = $"{(d.fromFallback ? "[폴백]" : "[AI]")}   {d.composition} / {d.topology} / x{d.difficultyModifier:0.00} / {d.tone}";
            _analysis.text = string.IsNullOrWhiteSpace(d.analysis) ? "분석 중…" : $"“{d.analysis}”";
        }
    }
}
