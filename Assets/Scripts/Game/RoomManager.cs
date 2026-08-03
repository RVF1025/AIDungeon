using System.Collections;
using UnityEngine;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>
    /// 층 루프: 웨이브 스폰 → 클리어 대기 → 프로파일 수집 → AI Director 호출 →
    /// 그 판단대로 다음 층 스폰. 이 한 바퀴가 프로젝트의 핵심 루프.
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        public int maxFloors = 5;

        private EnemySpawner _spawner;
        private GeminiDirectorClient _client;
        private BehaviorLogger _logger;
        private Health _playerHealth;

        private int _floor = 1;
        private string _state = "";
        private DirectorDecision _current;
        private GUIStyle _big, _small;

        public void Begin(EnemySpawner spawner, GeminiDirectorClient client,
                          BehaviorLogger logger, Health playerHealth)
        {
            _spawner = spawner; _client = client; _logger = logger; _playerHealth = playerHealth;
            StartCoroutine(RunGame());
        }

        private int EnemyCount(int floor) => Mathf.Min(3 + floor, 10);

        private static DirectorDecision IntroDecision() => new()
        {
            analysis = "첫 번째 방입니다. 당신이 어떻게 싸우는지 지켜보죠.",
            composition = Composition.Balanced,
            topology = Topology.Corridor,
            difficultyModifier = 1.0f,
            tone = Tone.Neutral,
        };

        private IEnumerator RunGame()
        {
            _current = IntroDecision();

            while (_floor <= maxFloors)
            {
                _logger.ResetFloor();
                _spawner.SpawnWave(_current, EnemyCount(_floor));
                _state = $"{_floor}층 — 전투";
                yield return null; // 스폰 반영 대기

                while (EnemyController.Active.Count > 0)
                {
                    if (_playerHealth.IsDead) { _state = "게임 오버"; yield break; }
                    yield return null;
                }
                if (_playerHealth.IsDead) { _state = "게임 오버"; yield break; }

                if (_floor == maxFloors) break;

                // --- 층 전환: 프로파일 수집 → AI 판단 ---
                _state = "AI Director 분석 중...";
                var profile = _logger.BuildProfile();
                Debug.Log($"[Floor {_floor} 클리어] {profile.ToPromptLine()}");
                yield return new WaitForSeconds(1.2f); // 암전 자리(연출은 4장에서)

                DirectorDecision next = null;
                yield return _client.RequestDecision(profile, d => next = d);
                _current = next ?? FallbackPresets.Build(profile);
                Debug.Log($"[AI Director] {_current}");

                _floor++;
            }

            _state = _playerHealth.IsDead ? "게임 오버" : "클리어! 🎉";
        }

        private void OnGUI()
        {
            _big ??= new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };

            GUI.Label(new Rect(12, 8, 600, 30),
                $"{_state}    HP {(_playerHealth != null ? Mathf.CeilToInt(_playerHealth.CurrentHp) : 0)}    적 {EnemyController.Active.Count}", _big);

            if (_current != null)
            {
                string tag = _current.fromFallback ? "[폴백]" : "[AI]";
                GUI.Label(new Rect(12, 40, 640, 26),
                    $"{tag} {_current.composition} / {_current.topology} / x{_current.difficultyModifier:0.00} / {_current.tone}", _small);
                GUI.Label(new Rect(12, 64, 640, 60), $"“{_current.analysis}”", _small);
            }

            GUI.Label(new Rect(12, Screen.height - 26, 700, 24),
                "WASD 이동 · 좌클릭 근접 · 우클릭 원거리", _small);
        }
    }
}
