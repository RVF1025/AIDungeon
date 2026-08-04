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

                // === 갈림길 선택 ===
                var profile = _logger.BuildProfile();
                Debug.Log($"[Floor {_floor} 클리어] {profile.ToPromptLine()}");

                var options = BuildOptions();
                int idx = 0;
                _phase = "갈림길 선택";
                yield return _select.Choose(options, "다음 갈림길을 고르시오.", i => idx = i);

                // === 다음 방: 전술은 결정론으로 즉시 계산(대기 X), 대사만 AI가 나중에 채움 ===
                int nextFloor = _floor + 1;
                var decision = BuildTactics(profile, nextFloor);
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
                _hud?.ShowDecision(_current);                          // 임시 대사 즉시 표시
                StartCoroutine(FetchDialogue(profile, nextFloor, decision)); // AI 대사는 백그라운드
                _floor++;
            }
        }

        // 전술(구성/방형태/난이도)은 결정론 — AI 없이 즉시. 대사는 임시(프리셋)로 채워두고 나중에 교체.
        private DirectorDecision BuildTactics(PlayerProfile p, int floor)
        {
            // analysis는 비워둠 → HUD가 "분석 중…" 표시, FetchDialogue가 한 번에 공개(AI 또는 프리셋).
            return new DirectorDecision
            {
                composition = DirectorPolicy.CanonicalComposition(p),
                topology = DirectorPolicy.ChooseTopology(p, floor),
                difficultyModifier = DirectorPolicy.CanonicalDifficulty(p),
                tone = DirectorPolicy.CanonicalTone(p),
                analysis = null,
            };
        }

        // AI 대사를 백그라운드로 받아 도착하면 HUD 갱신(실패하면 임시 대사 유지). 게임은 안 멈춤.
        private IEnumerator FetchDialogue(PlayerProfile profile, int floor, DirectorDecision target)
        {
            DirectorDecision res = null;
            yield return _client.RequestDecision(profile, floor, d => res = d);
            if (res == null) yield break;

            // AI 성공이든 폴백이든 res.analysis를 한 번에 공개("분석 중…" → 대사).
            target.analysis = res.analysis;
            target.tone = res.tone;
            target.fromFallback = res.fromFallback;
            if (_current == target) _hud?.ShowDecision(target);
            Debug.Log($"[AI Director] {target}");
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
            yield return null;

            while (EnemyController.Active.Count > 0)
            {
                if (_playerHealth.IsDead) yield break;
                yield return null;
            }
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
