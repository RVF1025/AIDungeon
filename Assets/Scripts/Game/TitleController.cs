using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AIDungeon.Game
{
    /// <summary>타이틀 씬: 클릭 또는 아무 키로 Game 씬 시작. 빈 씬에 이 컴포넌트만 붙이면 됨.</summary>
    public class TitleController : MonoBehaviour
    {
        private TextMeshProUGUI _prompt;
        private float _blink;

        private void Start()
        {
            EnsureCamera();
            var c = ScreenUi.BuildCanvas("TitleCanvas");
            ScreenUi.Label(c.transform, "AI DUNGEON", 120f, new Vector2(0, 130));
            ScreenUi.Label(c.transform, "AI 던전 디렉터 로그라이크", 40f, new Vector2(0, 30));
            _prompt = ScreenUi.Label(c.transform, "클릭하거나 아무 키를 눌러 시작", 44f, new Vector2(0, -170));
        }

        private void Update()
        {
            _blink += Time.deltaTime * 3f;
            if (_prompt != null)
            {
                var col = _prompt.color;
                col.a = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(_blink));
                _prompt.color = col;
            }

            bool key = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            if (key || click) SceneManager.LoadScene(GameSession.SceneGame);
        }

        private static void EnsureCamera()
        {
            var bg = new Color(0.05f, 0.05f, 0.09f);
            if (Camera.main == null)
            {
                var g = new GameObject("Main Camera");
                g.tag = "MainCamera";
                var cam = g.AddComponent<Camera>();
                cam.orthographic = true;
                cam.backgroundColor = bg;
            }
            else Camera.main.backgroundColor = bg;
        }
    }
}
