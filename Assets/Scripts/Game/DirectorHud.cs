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
        private string _personaName = "";
        private Image _portrait;
        private GameObject _portraitFrame;
        private Sprite _pTaunt, _pImpressed, _pConcern, _pNeutral;
        private bool _hasPortrait;

        /// <summary>페르소나 지정 + 초상(Resources/Portraits/{id}.png, 2x2 그리드) 로드.</summary>
        public void SetPersona(DirectorPersona persona)
        {
            _personaName = persona.name;
            LoadPortraits(persona.id);
        }

        private void LoadPortraits(string id)
        {
            _hasPortrait = false;
            var tex = Resources.Load<Texture2D>($"Portraits/{id}");
            if (tex == null) { if (_portrait != null) _portrait.enabled = false; return; }

            int hw = tex.width / 2, hh = tex.height / 2; // Unity 텍스처 좌표: (0,0)=좌하단
            // 우/하단 크롭(Gemini 워터마크 제거). 얼굴이 코너에 없게 뽑으면 손실 적음.
            const float mr = 0.10f, mb = 0.10f;
            int cw = Mathf.RoundToInt(hw * (1f - mr)), ch = Mathf.RoundToInt(hh * (1f - mb));
            int by = Mathf.RoundToInt(hh * mb);
            Sprite Slice(int x, int y) => Sprite.Create(tex, new Rect(x, y + by, cw, ch), new Vector2(0.5f, 0.5f), 100f);
            _pTaunt = Slice(0, hh);       // 좌상: 비웃음
            _pImpressed = Slice(hw, hh);  // 우상: 놀람
            _pConcern = Slice(0, 0);      // 좌하: 걱정
            _pNeutral = Slice(hw, 0);     // 우하: 무표정
            _hasPortrait = true;
            if (_portraitFrame != null) _portraitFrame.SetActive(true);
        }

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

            // 초상 액자(파란 테두리 + 어두운 바탕) → 배경 있는 초상도 자연스럽게
            var frame = MakePanel(panel, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(12, 0), new Vector2(200, 200), new Color(0.4f, 0.75f, 1f, 0.9f));
            var inner = MakePanel(frame, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-8, -8), new Color(0.03f, 0.03f, 0.06f, 1f));
            inner.GetComponent<Image>().raycastTarget = false;
            _portraitFrame = frame.gameObject;
            _portraitFrame.SetActive(false); // 초상 로드 성공 시에만 표시

            // 좌측 초상(액자 안). 초상 로드 전엔 숨김.
            var pgo = new GameObject("Portrait", typeof(RectTransform));
            pgo.transform.SetParent(panel, false);
            var prt = pgo.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0, 0);
            prt.anchoredPosition = new Vector2(18, 6);
            prt.sizeDelta = new Vector2(188, 188); // 상단 하늘색 테두리와 안 겹치게
            _portrait = pgo.AddComponent<Image>();
            _portrait.preserveAspect = true;
            _portrait.raycastTarget = false;
            _portrait.enabled = false;

            _tag = MakeText(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(260, -14), new Vector2(-290, 40), 26, TextAlignmentOptions.TopLeft);
            _tag.color = new Color(0.55f, 0.8f, 1f);

            _analysis = MakeText(panel, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(260, -56), new Vector2(-290, -70), 42, TextAlignmentOptions.TopLeft);

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
            string who = string.IsNullOrEmpty(_personaName) ? "" : $"감독: {_personaName}   ";
            _tag.text = $"{who}{(d.fromFallback ? "[폴백]" : "[AI]")}   {d.composition} / {d.topology} / x{d.difficultyModifier:0.00} / {d.tone}";
            _analysis.text = string.IsNullOrWhiteSpace(d.analysis) ? "분석 중..." : $"\"{d.analysis}\"";

            if (_hasPortrait && _portrait != null)
            {
                _portrait.sprite = d.tone switch
                {
                    Tone.Taunt => _pTaunt,
                    Tone.Impressed => _pImpressed,
                    Tone.Concern => _pConcern,
                    _ => _pNeutral,
                };
                _portrait.enabled = true;
            }
        }
    }
}
