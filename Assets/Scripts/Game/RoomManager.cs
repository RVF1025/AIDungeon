using System.Collections;
using UnityEngine;
using AIDungeon.Director;

namespace AIDungeon.Game
{
    /// <summary>
    /// 층 루프(무한): 매 층 새 위치에 방 생성(topology 반영) → 플레이어 이동 → 웨이브 스폰 →
    /// 클리어 대기 → 프로파일 수집 → AI Director 호출 → 그 판단이 다음 층 방+적을 설계.
    /// 플레이어 사망 시 종료.
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        private EnemySpawner _spawner;
        private GeminiDirectorClient _client;
        private BehaviorLogger _logger;
        private Health _playerHealth;
        private Rigidbody2D _playerRb;
        private DirectorHud _hud;

        private int _floor = 1;
        private string _phase = "";
        private bool _gameOver;
        private Room _room;

        public void Begin(EnemySpawner spawner, GeminiDirectorClient client,
                          BehaviorLogger logger, Health playerHealth, DirectorHud hud)
        {
            _spawner = spawner; _client = client; _logger = logger;
            _playerHealth = playerHealth; _hud = hud;
            _playerRb = playerHealth.GetComponent<Rigidbody2D>();
            StartCoroutine(RunGame());
        }

        private int EnemyCount(int floor) => Mathf.Min(3 + floor, 12);

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
            var current = IntroDecision();
            _hud?.ShowDecision(current);

            while (true) // 무한 — 사망 시에만 종료
            {
                // 이 층의 방을 새 위치에 생성(topology 반영) → 플레이어 이동
                if (_room != null) Destroy(_room.root);
                _room = RoomBuilder.Build(current, _floor);
                TeleportPlayer(_room.playerSpawn);
                CameraFollow.Instance?.Snap();
                _spawner.Configure(_room.center, _room.half);

                _logger.ResetFloor();
                _spawner.SpawnWave(current, EnemyCount(_floor));
                _phase = $"{_floor}층 — 전투";
                yield return null; // 스폰 반영 대기

                while (EnemyController.Active.Count > 0)
                {
                    if (_playerHealth.IsDead) { EndGame(); yield break; }
                    yield return null;
                }
                if (_playerHealth.IsDead) { EndGame(); yield break; }

                // --- 층 전환: 프로파일 수집 → AI 판단(다음 층 설계) ---
                _phase = "AI Director 분석 중...";
                var profile = _logger.BuildProfile();
                Debug.Log($"[Floor {_floor} 클리어] {profile.ToPromptLine()}");
                yield return new WaitForSeconds(1.2f); // 암전 자리(연출은 4장에서 확장)

                DirectorDecision next = null;
                yield return _client.RequestDecision(profile, d => next = d);
                current = next ?? FallbackPresets.Build(profile);
                _hud?.ShowDecision(current);
                Debug.Log($"[AI Director] {current}");

                _floor++;
            }
        }

        private void TeleportPlayer(Vector2 pos)
        {
            _playerHealth.transform.position = pos;
            if (_playerRb != null) _playerRb.linearVelocity = Vector2.zero;
        }

        private void EndGame()
        {
            _gameOver = true;
            _phase = $"게임 오버 — {_floor}층까지 도달";
        }

        private void Update()
        {
            if (_hud == null) return;
            int hp = _playerHealth != null ? Mathf.CeilToInt(_playerHealth.CurrentHp) : 0;
            _hud.SetStatus(_gameOver
                ? _phase
                : $"{_phase}    HP {hp}    적 {EnemyController.Active.Count}");
        }
    }
}
