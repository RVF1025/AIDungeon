using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AIDungeon.Game
{
    public enum PathKind { Combat, Elite, Rest }

    /// <summary>갈림길 노드 한 개. (확장: 보물/이벤트/상점, AI가 desc·구성에 개입)</summary>
    public class PathOption
    {
        public PathKind kind;
        public string title;
        public string desc;
    }

    /// <summary>
    /// 갈림길 선택 UI. 카드 2~3개를 띄우고 숫자 키로 고른다. 코드 생성(EventSystem 불필요).
    /// Choose를 코루틴으로 돌리면 선택될 때까지 대기 후 onChosen(index) 호출.
    /// </summary>
    public class PathSelectUI : MonoBehaviour
    {
        private int _chosen;

        public IEnumerator Choose(List<PathOption> options, string dialogue, Action<int> onChosen)
        {
            _chosen = -1;
            var canvas = Build(options, dialogue);

            while (_chosen < 0)
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.digit1Key.wasPressedThisFrame && options.Count >= 1) _chosen = 0;
                    else if (kb.digit2Key.wasPressedThisFrame && options.Count >= 2) _chosen = 1;
                    else if (kb.digit3Key.wasPressedThisFrame && options.Count >= 3) _chosen = 2;
                }
                yield return null;
            }

            Destroy(canvas.gameObject);
            onChosen?.Invoke(_chosen);
        }

        private Canvas Build(List<PathOption> options, string dialogue)
        {
            var canvas = ScreenUi.BuildCanvas("PathSelectCanvas");
            ScreenUi.Label(canvas.transform, dialogue, 44f, new Vector2(0, 340));
            ScreenUi.Label(canvas.transform, "숫자 키로 갈림길을 선택하시오", 28f, new Vector2(0, 280));

            int n = options.Count;
            const float spacing = 500f;
            float x0 = -(n - 1) * 0.5f * spacing;
            for (int i = 0; i < n; i++)
                BuildCard(canvas.transform, options[i], i + 1, new Vector2(x0 + i * spacing, -20));

            return canvas;
        }

        private void BuildCard(Transform parent, PathOption opt, int num, Vector2 pos)
        {
            var card = new GameObject($"Card{num}", typeof(RectTransform));
            card.transform.SetParent(parent, false);
            var rt = card.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(440, 440);
            var img = card.AddComponent<Image>();
            img.color = KindColor(opt.kind);

            ScreenUi.Label(card.transform, $"[{num}]", 46f, new Vector2(0, 150), 400f);
            ScreenUi.Label(card.transform, opt.title, 54f, new Vector2(0, 55), 400f);
            ScreenUi.Label(card.transform, opt.desc, 30f, new Vector2(0, -60), 400f);
        }

        private static Color KindColor(PathKind kind) => kind switch
        {
            PathKind.Elite => new Color(0.45f, 0.12f, 0.14f, 0.95f),
            PathKind.Rest => new Color(0.12f, 0.32f, 0.22f, 0.95f),
            _ => new Color(0.16f, 0.18f, 0.26f, 0.95f), // Combat
        };
    }
}
