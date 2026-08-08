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
        private DirectorPersona _persona;

        private int _floor = 1;
        private string _phase = "";
        private Room _room;
        private DirectorDecision _current;
        private ForkArchetype _arch = ForkArchetypes.Normal; // 이번 층 갈림길 유형(적수/회복 등)
        private string _lastComp = "", _lastTopo = ""; // 직전 층(변화 보장용)
        private LoadingScreen _loading;
        private const float MinLoadSeconds = 2.4f;

        public void Begin(EnemySpawner spawner, GeminiDirectorClient client,
                          BehaviorLogger logger, Health playerHealth, DirectorHud hud)
        {
            _spawner = spawner; _client = client; _logger = logger;
            _playerHealth = playerHealth; _hud = hud;
            _playerRb = playerHealth.GetComponent<Rigidbody2D>();
            _select = gameObject.AddComponent<PathSelectUI>();
            _persona = DirectorPersonas.Random(); // 이번 런의 디렉터
            _hud?.SetPersona(_persona);
            Debug.Log($"[Director] 이번 런: {_persona.name}");
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
            _current.analysis = _persona.Intro(); // 인트로도 페르소나 목소리
            _lastComp = _current.composition; _lastTopo = _current.topology;
            _hud?.ShowDecision(_current);

            while (true) // 무한 — 사망 시에만 종료
            {
                // === 전투 페이즈 ===
                yield return RunCombat(_current);
                if (_playerHealth.IsDead) { EndGame(); yield break; }

                // === 클리어 → AI가 갈림길 설계 → 배너(2초) → 선택 → 코드가 수치 구성 ===
                var profile = _logger.BuildProfile();
                Debug.Log($"[Floor {_floor} 클리어] {profile.ToPromptLine()}");

                int nextFloor = _floor + 1;

                // 클리어 순간부터 AI가 갈림길 선택지를 설계(유형 선택 + 성향 말투). 실패 시 기본 3종.
                List<ForkChoice> choices = null; bool forkReady = false;
                StartCoroutine(_client.RequestForkOptions(profile, _persona, c => { choices = c; forkReady = true; }));

                _phase = $"{_floor}층 클리어!";
                yield return ShowClearBanner(); // 이 동안 설계 도착(레이턴시 은폐)
                while (!forkReady) yield return null;

                var options = ToOptions(choices);
                int idx = 0;
                _phase = "갈림길 선택";
                yield return _select.Choose(options, _persona.Fork(), i => idx = i); // 갈림길 유도 대사는 로컬(즉시)

                var opt = options[idx];
                _arch = ForkArchetypes.ById(opt.archetypeId);

                // 선택한 유형의 '검증된 수치'로 다음 층을 코드가 구성. 전술=정책, 대사/톤=AI 저작.
                float diff = Mathf.Clamp(DirectorPolicy.CanonicalDifficulty(profile) * _arch.diffMul, 0.8f, 1.6f);
                var decision = new DirectorDecision
                {
                    composition = DirectorPolicy.CompositionAvoiding(profile, _lastComp),
                    topology = DirectorPolicy.ChooseTopologyAvoiding(profile, nextFloor, _lastTopo),
                    difficultyModifier = diff,
                    tone = DirectorPolicy.IsValidTone(opt.tone) ? opt.tone : Tone.Neutral,
                    analysis = opt.line,
                };
                bool elite = DirectorPolicy.WillSpawnElite(nextFloor, diff);
                if (!elite && decision.tone == Tone.Taunt) // 도발은 정예급만 → 강등 시 대사도 톤에 맞춤
                {
                    decision.tone = DirectorPolicy.NonTauntTone(profile);
                    decision.analysis = _persona.Fallback(decision.tone);
                }
                if (string.IsNullOrWhiteSpace(decision.analysis)) decision.analysis = _persona.Fallback(decision.tone);

                // 전환 페이싱용 로딩(AI 재요청은 없음 — 대사는 이미 확정).
                _phase = "다음 스테이지 준비...";
                if (_loading == null) _loading = new GameObject("Loading").AddComponent<LoadingScreen>();
                float t0 = Time.time;
                while (Time.time - t0 < MinLoadSeconds) yield return null;

                if (_arch.heal01 > 0f) _playerHealth.Heal(_playerHealth.maxHp * _arch.heal01);

                _current = decision;
                _lastComp = _current.composition; _lastTopo = _current.topology;
                _hud?.ShowDecision(_current);
                Debug.Log($"[AI Director] 갈림길:{_arch.id} → {_current}");
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
            int count = Mathf.Max(1, Mathf.RoundToInt(EnemyCount(_floor) * _arch.countMul)); // 유형별 적 수 배수
            _spawner.SpawnWave(decision, count, _floor);
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

        // AI가 설계한 갈림길 선택지(ForkChoice) → UI용 PathOption 변환.
        private List<PathOption> ToOptions(List<ForkChoice> choices)
        {
            var list = new List<PathOption>(choices.Count);
            foreach (var c in choices)
                list.Add(new PathOption
                {
                    archetypeId = c.id,
                    kind = KindOf(c.id), // 카드 색상용
                    title = c.title,
                    desc = c.desc,
                    line = c.line,
                    tone = c.tone,
                });
            return list;
        }

        private static PathKind KindOf(string archId) =>
            archId == ForkArchetypes.Elite.id ? PathKind.Elite :
            archId == ForkArchetypes.Rest.id ? PathKind.Rest : PathKind.Combat;

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
