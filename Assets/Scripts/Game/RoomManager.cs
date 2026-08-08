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
        private PlayerController _playerCtrl;
        private DirectorHud _hud;
        private PathSelectUI _select;
        private DirectorPersona _persona;

        private int _floor = 1;
        private string _phase = "";
        private Room _room;
        private DirectorDecision _current;
        private ForkArchetype _arch = ForkArchetypes.Normal; // 이번 층 방 유형(적수/보물 등)
        private string _lastArchId = "";                     // 직전 노드 유형(휴식 연속 방지)
        private string _lastComp = "", _lastTopo = ""; // 직전 층(변화 보장용)
        private LoadingScreen _loading;
        private const float MinLoadSeconds = 2.4f;

        public void Begin(EnemySpawner spawner, GeminiDirectorClient client,
                          BehaviorLogger logger, Health playerHealth, DirectorHud hud)
        {
            _spawner = spawner; _client = client; _logger = logger;
            _playerHealth = playerHealth; _hud = hud;
            _playerRb = playerHealth.GetComponent<Rigidbody2D>();
            _playerCtrl = playerHealth.GetComponent<PlayerController>();
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

                // 정예/보물 방을 클리어했다면 보물상자
                if (_arch.treasure) yield return OpenTreasure();

                var profile = _logger.BuildProfile();
                Debug.Log($"[Floor {_floor} 클리어] {profile.ToPromptLine()}");

                _phase = $"{_floor}층 클리어!";
                yield return ShowClearBanner();

                // 갈림길 시퀀스: 휴식이면 회복 후 다시 갈림길, 전투형이 선택되면 다음 층 확정
                yield return ForkSequence(profile);
                if (_playerHealth.IsDead) { EndGame(); yield break; }

                _floor++;
            }
        }

        // 갈림길: AI가 제시된 선택지를 평가(초상+채팅) → 플레이어 선택 → 휴식이면 회복 후 반복,
        // 전투형(일반/정예/???)이면 다음 층 전투를 구성하고 반환.
        private IEnumerator ForkSequence(PlayerProfile profile)
        {
            while (true)
            {
                var cands = ForkArchetypes.Select(_persona.id, profile.avgHpPct, _lastArchId);

                // AI가 이 갈림길을 평가(대기 필요 → 로딩 화면).
                ForkComment comment = null; bool ready = false;
                StartCoroutine(_client.RequestForkComment(profile, _persona, cands, c => { comment = c; ready = true; }));
                _phase = "AI Director가 갈림길을 살피는 중...";
                if (_loading == null) _loading = new GameObject("Loading").AddComponent<LoadingScreen>();
                float t0 = Time.time;
                while (!ready || Time.time - t0 < 0.8f) yield return null;
                if (_loading != null) { Destroy(_loading.gameObject); _loading = null; }

                var options = ToOptions(cands);
                Sprite portrait = _hud != null && _hud.HasPortrait ? _hud.PortraitFor(comment.tone) : null;
                int idx = 0;
                _phase = "갈림길 선택";
                yield return _select.Choose(options, _hud != null ? _hud.PersonaName : "",
                                            comment.line, portrait, i => idx = i);

                var arch = cands[idx];
                _lastArchId = arch.id;

                if (!arch.combat) // 휴식: 전투 없이 회복 + 문구 → 다시 갈림길(연속 휴식은 제외됨)
                {
                    if (arch.heal01 > 0f) _playerHealth.Heal(_playerHealth.maxHp * arch.heal01);
                    yield return ShowRestMessage();
                    profile = _logger.BuildProfile(); // 회복 반영
                    continue;
                }

                yield return BuildNextCombat(profile, arch);
                yield break;
            }
        }

        // 선택된 전투형 유형으로 다음 층 전투를 구성. ???는 진입 시 무작위로 해석하고 정체를 공개.
        private IEnumerator BuildNextCombat(PlayerProfile profile, ForkArchetype arch)
        {
            int nextFloor = _floor + 1;
            float diffMul = arch.diffMul, countMul = arch.countMul;
            bool treasure = arch.treasure;
            string reveal = null;

            if (arch.mystery)
            {
                var m = ForkArchetypes.ResolveMystery();
                diffMul = m.diffMul; countMul = m.countMul; treasure = m.treasure; reveal = m.reveal;
                Debug.Log($"[???] 해석: {reveal}");
            }

            // 이번 전투 방의 유효 유형(적수/보물)을 보관 → RunCombat/보물상자에서 사용.
            _arch = new ForkArchetype { id = arch.id, combat = true, diffMul = diffMul, countMul = countMul, treasure = treasure };

            float diff = Mathf.Clamp(DirectorPolicy.CanonicalDifficulty(profile) * diffMul, 0.8f, 1.6f);
            bool elite = DirectorPolicy.WillSpawnElite(nextFloor, diff);
            string tone = DirectorPolicy.CanonicalTone(profile);
            if (!elite && tone == Tone.Taunt) tone = DirectorPolicy.NonTauntTone(profile);

            var decision = new DirectorDecision
            {
                composition = DirectorPolicy.CompositionAvoiding(profile, _lastComp),
                topology = DirectorPolicy.ChooseTopologyAvoiding(profile, nextFloor, _lastTopo),
                difficultyModifier = diff,
                tone = tone,
                // ??? 방은 정체 공개, 그 외엔 성향 대사.
                analysis = !string.IsNullOrEmpty(reveal) ? reveal : _persona.Fallback(tone),
            };

            // 전환 페이싱용 로딩(AI 재요청 없음).
            _phase = "다음 스테이지 준비...";
            if (_loading == null) _loading = new GameObject("Loading").AddComponent<LoadingScreen>();
            float t0 = Time.time;
            while (Time.time - t0 < MinLoadSeconds) yield return null;

            _current = decision;
            _lastComp = _current.composition; _lastTopo = _current.topology;
            _hud?.ShowDecision(_current);
            Debug.Log($"[AI Director] 방:{_arch.id} → {_current}");
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

        // 후보 유형 → UI용 PathOption(고정 제목/설명).
        private List<PathOption> ToOptions(List<ForkArchetype> cands)
        {
            var list = new List<PathOption>(cands.Count);
            foreach (var a in cands)
                list.Add(new PathOption
                {
                    archetypeId = a.id,
                    kind = KindOf(a.id), // 카드 색상용
                    title = a.title,
                    desc = a.desc,
                });
            return list;
        }

        private static PathKind KindOf(string archId) =>
            archId == ForkArchetypes.Elite.id ? PathKind.Elite :
            archId == ForkArchetypes.Rest.id ? PathKind.Rest : PathKind.Combat;

        // 휴식 공간: 전투 없이 회복 문구만 보여주고 다음 갈림길로.
        private IEnumerator ShowRestMessage()
        {
            var canvas = ScreenUi.BuildCanvas("RestCanvas", 0.5f);
            canvas.sortingOrder = 250;
            var t = ScreenUi.Label(canvas.transform, "휴식 공간", 84f, new Vector2(0, 40));
            t.color = new Color(0.6f, 1f, 0.8f);
            ScreenUi.Label(canvas.transform, "잠시 숨을 돌렸다. 체력을 회복했다.", 36f, new Vector2(0, -50));
            yield return new WaitForSeconds(2f);
            Destroy(canvas.gameObject);
        }

        // 정예/보물 방 클리어 보상: 근접↑/원거리↑/회복 중 하나(칼·지팡이·포션 아이콘).
        private const int SwordTile = 103, StaffTile = 130, PotionTile = 114; // Kenney tiny dungeon (필요시 조정)
        private IEnumerator OpenTreasure()
        {
            int r = Random.Range(0, 3);
            int tile; string label;
            switch (r)
            {
                case 0:
                    if (_playerCtrl != null) _playerCtrl.meleeDamage += 8f;
                    tile = SwordTile; label = "근접 공격력 +8"; break;
                case 1:
                    if (_playerCtrl != null) _playerCtrl.rangedDamage += 6f;
                    tile = StaffTile; label = "원거리 공격력 +6"; break;
                default:
                    _playerHealth.Heal(_playerHealth.maxHp * 0.3f);
                    tile = PotionTile; label = "체력 30% 회복"; break;
            }

            var canvas = ScreenUi.BuildCanvas("TreasureCanvas", 0.6f);
            canvas.sortingOrder = 260;
            var title = ScreenUi.Label(canvas.transform, "보물상자!", 80f, new Vector2(0, 150));
            title.color = new Color(1f, 0.85f, 0.35f);

            var igo = new GameObject("RewardIcon", typeof(RectTransform));
            igo.transform.SetParent(canvas.transform, false);
            var irt = igo.GetComponent<RectTransform>();
            irt.anchoredPosition = new Vector2(0, 10);
            irt.sizeDelta = new Vector2(180, 180);
            var img = igo.AddComponent<UnityEngine.UI.Image>();
            img.sprite = SpriteFactory.Tile(tile);
            img.preserveAspect = true;

            ScreenUi.Label(canvas.transform, label, 44f, new Vector2(0, -140));
            yield return new WaitForSeconds(2.2f);
            Destroy(canvas.gameObject);
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
