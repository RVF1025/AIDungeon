using TMPro;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>층 전환 로딩 오버레이. 생성하면 표시, Destroy하면 사라짐.</summary>
    public class LoadingScreen : MonoBehaviour
    {
        private TextMeshProUGUI _dots;
        private float _t;
        private const string Base = "다음 층을 설계하는 중";

        private void Awake()
        {
            var canvas = ScreenUi.BuildCanvas("LoadingCanvas");
            canvas.sortingOrder = 300; // HUD/선택 위
            canvas.transform.SetParent(transform, false); // 생명주기 종속(Destroy 시 함께)

            ScreenUi.Label(canvas.transform, "AI DIRECTOR", 42f, new Vector2(0, 70));
            _dots = ScreenUi.Label(canvas.transform, Base + "…", 34f, new Vector2(0, -10));
        }

        private void Update()
        {
            _t += Time.deltaTime;
            int n = 1 + Mathf.FloorToInt(_t * 2f) % 3; // 점 1~3개 애니메이션
            _dots.text = Base + new string('.', n);
        }
    }
}
