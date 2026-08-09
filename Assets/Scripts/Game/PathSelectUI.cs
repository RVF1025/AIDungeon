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

    /// <summary>
    /// 갈림길 노드 한 개. 제목/설명은 고정 문구(코드). 수치는 archetypeId에 매핑된 ForkArchetype이 소유.
    /// 감독의 평가 대사는 카드가 아니라 상단 채팅에 별도로 표시된다.
    /// </summary>
    public class PathOption
    {
        public PathKind kind;         // 카드 색상용
        public string archetypeId;    // ForkArchetypes 풀의 유형 id
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

        public IEnumerator Choose(List<PathOption> options, string personaName, string comment,
                                  Sprite portrait, Action<int> onChosen)
        {
            _chosen = -1;
            var canvas = Build(options, personaName, comment, portrait);

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

        private Canvas Build(List<PathOption> options, string personaName, string comment, Sprite portrait)
        {
            var canvas = ScreenUi.BuildCanvas("PathSelectCanvas");

            // 상단 감독 대사창(전투 HUD와 동일한 스타일: 초상 옆에 패널+텍스트)
            var panel = MakePanel(canvas.transform, new Vector2(0, 330), new Vector2(1500, 240),
                new Color(0.05f, 0.06f, 0.12f, 0.92f));
            // 상단 파란 테두리 느낌
            MakePanel(panel, new Vector2(0, 122), new Vector2(1500, 4), new Color(0.4f, 0.75f, 1f, 0.9f));

            if (portrait != null)
            {
                var pgo = new GameObject("ForkPortrait", typeof(RectTransform));
                pgo.transform.SetParent(panel, false);
                var prt = pgo.GetComponent<RectTransform>();
                prt.anchoredPosition = new Vector2(-620, 0);
                prt.sizeDelta = new Vector2(200, 200);
                var pImg = pgo.AddComponent<Image>();
                pImg.sprite = portrait;
                pImg.preserveAspect = true;
                pImg.raycastTarget = false;
            }

            float textX = portrait != null ? 90f : 0f; // 초상 있으면 오른쪽으로
            float textW = portrait != null ? 1120f : 1400f;
            if (!string.IsNullOrEmpty(personaName))
            {
                var name = ScreenUi.Label(panel, $"감독: {personaName}", 28f, new Vector2(textX, 72), textW);
                name.color = new Color(0.55f, 0.8f, 1f);
                name.alignment = TextAlignmentOptions.Left;
            }
            var line = ScreenUi.Label(panel, string.IsNullOrEmpty(comment) ? "" : $"\"{comment}\"",
                42f, new Vector2(textX, -8), textW);
            line.alignment = TextAlignmentOptions.Left;

            ScreenUi.Label(canvas.transform, "숫자 키로 갈림길을 선택하시오", 26f, new Vector2(0, 170));

            int n = options.Count;
            const float spacing = 500f;
            float x0 = -(n - 1) * 0.5f * spacing;
            for (int i = 0; i < n; i++)
                BuildCard(canvas.transform, options[i], i + 1, new Vector2(x0 + i * spacing, -70));

            return canvas;
        }

        private RectTransform MakePanel(Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
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
