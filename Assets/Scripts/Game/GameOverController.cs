using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AIDungeon.Game
{
    /// <summary>게임오버 씬: 도달 층 표시, R/클릭=재시작, ESC=타이틀. 빈 씬에 이 컴포넌트만.</summary>
    public class GameOverController : MonoBehaviour
    {
        private void Start()
        {
            EnsureCamera();
            var c = ScreenUi.BuildCanvas("GameOverCanvas");
            var title = ScreenUi.Label(c.transform, "GAME OVER", 110f, new Vector2(0, 150));
            title.color = new Color(1f, 0.4f, 0.4f);
            ScreenUi.Label(c.transform, $"{GameSession.FloorsReached}층까지 도달했습니다", 48f, new Vector2(0, 30));
            ScreenUi.Label(c.transform, "R  다시 시작        ESC  타이틀", 40f, new Vector2(0, -160));
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.rKey.wasPressedThisFrame) SceneManager.LoadScene(GameSession.SceneGame);
            else if (kb.escapeKey.wasPressedThisFrame) SceneManager.LoadScene(GameSession.SceneTitle);
        }

        private static void EnsureCamera()
        {
            var bg = new Color(0.06f, 0.03f, 0.05f);
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
