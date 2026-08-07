using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private PathSelectUI _select;

        private int _floor = 1;
        private string _phase = "";
        private Room _room;
        private DirectorDecision _current;
        private LoadingScreen _loading;
        private const float MinLoadSeconds = 1.2f;

        public void Begin(EnemySpawner spawner, GeminiDirectorClient client,
                          BehaviorLogger logger, Health playerHealth, DirectorHud hud)
        {
            _spawner = spawner; _client = client; _logger = logger;
            _playerHealth = playerHealth; _hud = hud;
            _playerRb = playerHealth.GetComponent<Rigidbody2D>();
            _select = gameObject.AddComponent<PathSelectUI>();
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
            _current = IntroDecision();
            _hud?.ShowDecision(_current);

            while (true) // 무한 — 사망 시에만 종료
            {
                // === 전투 페이즈 ===
                yield return RunCombat(_current);
                if (_playerHealth.IsDead) { EndGame(); yield break; }

                // === 클리어 → AI 선요청 → 클리어 배너(2초) → 갈림길 선택 ===
                var profile = _logger.BuildProfile();
                Debug.Log($"[Floor {_floor} 클리어] {profile.ToPromptLine()}");

                // 클리어 순간부터 AI 요청 시작(배너+선택 동안 레이턴시 은폐)
                int nextFloor = _floor + 1;
                DirectorDecision res = null; bool ready = false;
                StartCoroutine(_client.RequestDecision(profile, nextFloor, d => { res = d; ready = true; }));

                _phase = $"{_floor}층 클리어!";
                yield return ShowClearBanner();

                var options = BuildOptions();
                int idx = 0;
                _phase = "갈림길 선택";
                yield return _select.Choose(options, "다음 갈림길을 고르시오.", i => idx = i);

                // === 로딩 화면 표시 + 대사 대기(최소 표시시간 보장, 방은 RunCombat에서 준비되면 해제) ===
                _phase = "AI Director 분석 중...";
                if (_loading == null) _loading = new GameObject("Loading").AddComponent<LoadingScreen>();
                float t0 = Time.time;
                while (!ready || Time.time - t0 < MinLoadSeconds) yield return null;
                var decision = res ?? FallbackPresets.Build(profile);

                switch (options[idx].kind)
                {
                    case PathKind.Rest:
                        _playerHealth.Heal(_playerHealth.maxHp * 0.4f);
                        break;
                    case PathKind.Elite:
                        decision.difficultyModifier = Mathf.Clamp(decision.difficultyModifier * 1.3f, 0.8f, 1.6f);
                        break;
                }

                _current = decision;
                _hud?.ShowDecision(_current);
                Debug.Log($"[AI Director] {_current}");
                _floor++;
            }
        }

        // 한 전투 방: 생성 → 이동 → 스폰 → 전멸 대기 (사망 시 즉시 반환)
        private IEnumerator RunCombat(DirectorDecision decision)
        {
            if (_room != null) Destroy(_room.root);
            _room = RoomBuilder.Build(decision, _floor);
            TeleportPlayer(_room.playerSpawn);
            CameraFollow.Instance?.Snap();
            _spawner.Configure(_room.center, _room.half);

            _logger.ResetFloor();
            _spawner.SpawnWave(decision, EnemyCount(_floor));
            _phase = $"{_floor}층 — 전투";
            yield return null; // 스폰 반영

            if (_loading != null) { Destroy(_loading.gameObject); _loading = null; } // 방 준비됐으니 로딩 해제

            while (EnemyController.Active.Count > 0)
            {
                if (_playerHealth.IsDead) yield break;
                yield return null;
            }
        }

        // 스테이지 클리어 배너 2초 (방을 살짝만 어둡게 → 전장이 보임)
        private IEnumerator ShowClearBanner()
        {
            var canvas = ScreenUi.BuildCanvas("ClearCanvas", 0.4f);
            canvas.sortingOrder = 250;
            var t = ScreenUi.Label(canvas.transform, $"{_floor}층 클리어!", 96f, new Vector2(0, 30));
            t.color = new Color(0.5f, 1f, 0.75f);
            ScreenUi.Label(canvas.transform, "적을 모두 처치했습니다", 34f, new Vector2(0, -60));
            yield return new WaitForSeconds(2f);
            Destroy(canvas.gameObject);
        }

        // 갈림길 후보 (스켈레톤: 전투/정예/휴식. 확장: 보물·이벤트·상점, AI 개입)
        private List<PathOption> BuildOptions()
        {
            return new List<PathOption>
            {
                new PathOption { kind = PathKind.Combat, title = "전투",     desc = "평범한 다음 방" },
                new PathOption { kind = PathKind.Elite,  title = "정예 전투", desc = "더 강한 적 (난이도↑)" },
                new PathOption { kind = PathKind.Rest,   title = "휴식",     desc = "체력 40% 회복" },
            };
        }

        private void TeleportPlayer(Vector2 pos)
        {
            _playerHealth.transform.position = pos;
            if (_playerRb != null) _playerRb.linearVelocity = Vector2.zero;
        }

        private void EndGame()
        {
            GameSession.FloorsReached = _floor;
            SceneManager.LoadScene(GameSession.SceneGameOver);
        }

        private void Update()
        {
            if (_hud == null) return;
            int hp = _playerHealth != null ? Mathf.CeilToInt(_playerHealth.CurrentHp) : 0;
            _hud.SetStatus($"{_phase}    HP {hp}    적 {EnemyController.Active.Count}");
        }
    }
}
