using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AIDungeon.Game
{
    /// <summary>타이틀/게임오버용 풀스크린 캔버스 + 중앙 정렬 라벨을 코드로 생성.</summary>
    public static class ScreenUi
    {
        public static Canvas BuildCanvas(string name)
        {
            var go = new GameObject(name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 200;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            var bg = new GameObject("BG", typeof(RectTransform));
            bg.transform.SetParent(go.transform, false);
            var rt = bg.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = bg.AddComponent<Image>();
            img.color = new Color(0.05f, 0.05f, 0.09f, 1f);
            return c;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Vector2 pos)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(1700, size * 1.6f);
            var t = go.AddComponent<TextMeshProUGUI>();
            var f = UiFont.Korean();
            if (f != null) t.font = f;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }
    }
}
