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
        private bool _eliteRoom;                              // 이번 방이 정예 스폰 방인지(정예 전투/정예 매복)
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

                var profile = _logger.BuildProfile();
                Debug.Log($"[Floor {_floor} 클리어] {profile.ToPromptLine()}");

                _phase = $"{_floor}층 클리어!";
                yield return ShowClearBanner();

                // 정예 전투 등 보상 방을 클리어했다면 클리어 배너 다음에 보물상자
                if (_arch.treasure) yield return OpenTreasure();

                // 갈림길 시퀀스: 휴식/보물방이면 처리 후 다시 갈림길, 전투형이면 다음 층 확정
                yield return ForkSequence(profile);
                if (_playerHealth.IsDead) { EndGame(); yield break; }

                _floor++;
            }
        }

        // 갈림길: AI가 제시된 선택지를 평가(초상+채팅) → 플레이어 선택 → 휴식이면 회복 후 반복,
        // 전투형(일반/정예/???)이면 다음 층 전투를 구성하고 반환.
        private IEnumerator ForkSequence(PlayerProfile profile)
        {
            string recentEvent = null; // 직전 노드가 휴식/보물이면 그 사건에 반응(전투 재평가 방지)
            while (true)
            {
                var cands = ForkArchetypes.Select(_persona.id, profile.avgHpPct, _lastArchId);

                // AI가 이 갈림길을 평가(대기 필요 → 로딩 화면).
                ForkComment comment = null; bool ready = false;
                StartCoroutine(_client.RequestForkComment(profile, _persona, cands, recentEvent, c => { comment = c; ready = true; }));

                // 다음 전투 진입 대사도 선택지별로 병렬 선요청(고르는 즉시 사용). composition/topology는
                // 선택지 무관하게 동일하므로 정예 여부만 달리해 요청한다. 휴식/???는 제외.
                int nextFloor = _floor + 1;
                string entryComp = DirectorPolicy.CompositionAvoiding(profile, _lastComp);
                string entryTopo = DirectorPolicy.ChooseTopologyAvoiding(profile, nextFloor, _lastTopo);
                if (entryTopo == Topology.Corridor && entryComp == Composition.RusherPack) entryComp = Composition.KiterPack;
                var entry = new ForkComment[cands.Count];
                var entryReady = new bool[cands.Count];
                for (int i = 0; i < cands.Count; i++)
                {
                    var a = cands[i];
                    if (!a.combat || a.mystery) { entryReady[i] = true; continue; }
                    int oi = i; bool el = a.id == ForkArchetypes.Elite.id;
                    StartCoroutine(_client.RequestCombatEntry(profile, _persona, entryComp, el,
                        c => { entry[oi] = c; entryReady[oi] = true; }));
                }

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
                                            comment.line, portrait,
                                            Mathf.CeilToInt(_playerHealth.CurrentHp), Mathf.CeilToInt(_playerHealth.maxHp),
                                            i => idx = i);

                var arch = cands[idx];
                _lastArchId = arch.id;

                if (!arch.combat) // 휴식: 전투 없이 회복 + 문구 → 다시 갈림길(연속 휴식은 제외됨)
                {
                    int before = Mathf.CeilToInt(_playerHealth.CurrentHp);
                    if (arch.heal01 > 0f) _playerHealth.Heal(_playerHealth.maxHp * arch.heal01);
                    int after = Mathf.CeilToInt(_playerHealth.CurrentHp);
                    yield return ShowRestMessage(before, after);
                    profile = _logger.BuildProfile(); // 회복 반영
                    recentEvent = "방금 휴식 공간에서 체력을 회복했다"; // 다음 갈림길은 이 사건에 반응
                    continue;
                }

                if (arch.mystery)
                {
                    var m = ForkArchetypes.ResolveMystery();
                    Debug.Log($"[???] 해석: {m.reveal}");

                    if (!m.combat) // 비전투 결과: 보물 또는 회복 후 다시 갈림길
                    {
                        if (m.heal01 > 0f)
                        {
                            int hb = Mathf.CeilToInt(_playerHealth.CurrentHp);
                            _playerHealth.Heal(_playerHealth.maxHp * m.heal01);
                            int ha = Mathf.CeilToInt(_playerHealth.CurrentHp);
                            yield return ShowRestMessage(hb, ha, "???", m.reveal);
                            recentEvent = "방금 ??? 방에서 체력을 회복했다";
                        }
                        else // 보물
                        {
                            if (m.treasure) yield return OpenTreasure();
                            recentEvent = "방금 ??? 방에서 뜻밖의 보물을 얻었다";
                        }
                        profile = _logger.BuildProfile();
                        continue;
                    }

                    // 전투형 ???: 정체를 명확히 공개(고정 문구) + AI가 뒤에 붙일 반응 한마디.
                    ForkComment cm = null; bool mready = false;
                    string situation = $"??? 방의 정체가 '{m.reveal}'로 밝혀졌다. 정체 문구는 이미 화면에 공개되니, " +
                                       "그 뒤에 자연스럽게 이어붙일 네 말투의 반응 한마디만 작성하라(정체를 다시 설명하지 말 것).";
                    StartCoroutine(_client.RequestSituationComment(profile, _persona, situation, c => { cm = c; mready = true; }));
                    _phase = "AI Director가 상황을 살피는 중...";
                    if (_loading == null) _loading = new GameObject("Loading").AddComponent<LoadingScreen>();
                    float ts = Time.time;
                    while (!mready || Time.time - ts < 0.8f) yield return null;
                    if (_loading != null) { Destroy(_loading.gameObject); _loading = null; }

                    string mtone = cm.tone;
                    if (mtone == Tone.Impressed) mtone = Tone.Neutral;                 // 감탄은 갈림길 평가 전용
                    if (!m.elite && mtone == Tone.Taunt) mtone = DirectorPolicy.NonTauntTone(profile); // 도발은 정예만
                    string mline = $"{m.reveal} {cm.line}"; // 명확한 정체 공개 + AI 반응
                    yield return EnterCombat(profile, arch.id, m.diffMul, m.countMul, m.treasure, m.elite, mline, mtone);
                    yield break;
                }

                // 일반/정예 전투: 선요청해 둔 AI 진입 대사 사용(대개 이미 도착, 아니면 잠깐 대기).
                bool eliteRoom = arch.id == ForkArchetypes.Elite.id;
                if (!entryReady[idx])
                {
                    _phase = "다음 전투 준비...";
                    if (_loading == null) _loading = new GameObject("Loading").AddComponent<LoadingScreen>();
                    while (!entryReady[idx]) yield return null;
                }
                var ec = entry[idx] ?? new ForkComment { tone = Tone.Neutral, line = _persona.Fallback(Tone.Neutral) };
                yield return EnterCombat(profile, arch.id, arch.diffMul, arch.countMul, arch.treasure, eliteRoom,
                                         ec.line, ec.tone);
                yield break;
            }
        }

        // 다음 층 전투를 구성(전술=정책, 난이도=층 점진 반영, 대사/톤=인자). 방 유형 수치를 _arch에 보관.
        private IEnumerator EnterCombat(PlayerProfile profile, string archId, float diffMul, float countMul,
                                        bool treasure, bool eliteRoom, string analysis, string tone)
        {
            int nextFloor = _floor + 1;
            _eliteRoom = eliteRoom;
            _arch = new ForkArchetype { id = archId, combat = true, diffMul = diffMul, countMul = countMul, treasure = treasure };

            float diff = DirectorPolicy.FloorScaledDifficulty(profile, nextFloor, diffMul); // 층 오를수록 ↑
            string comp = DirectorPolicy.CompositionAvoiding(profile, _lastComp);
            string topo = DirectorPolicy.ChooseTopologyAvoiding(profile, nextFloor, _lastTopo);
            // 통로는 근접 없음 → 근접 위주 rusher_pack은 어울리지 않으니 kiter_pack으로 교체.
            if (topo == Topology.Corridor && comp == Composition.RusherPack) comp = Composition.KiterPack;

            var decision = new DirectorDecision
            {
                composition = comp,
                topology = topo,
                difficultyModifier = diff,
                tone = tone,
                analysis = analysis,
            };

            // 전환 페이싱용 로딩.
            _phase = "다음 스테이지 준비...";
            if (_loading == null) _loading = new GameObject("Loading").AddComponent<LoadingScreen>();
            float t0 = Time.time;
            while (Time.time - t0 < MinLoadSeconds) yield return null;

            _current = decision;
            _lastComp = _current.composition; _lastTopo = _current.topology;
            _hud?.ShowDecision(_current);
            Debug.Log($"[AI Director] 방:{_arch.id} 정예:{_eliteRoom} → {_current}");
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
            // 정예는 정예 방에서만, 대신 비중 상승(약 절반). 그 외 방은 0.
            int eliteCount = _eliteRoom ? Mathf.Clamp(count / 2, 2, count) : 0;
            _spawner.SpawnWave(decision, count, eliteCount);
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

        // 회복 문구(전투 없이 회복량 표시). 휴식 공간과 ??? 회복에서 공용.
        private IEnumerator ShowRestMessage(int before, int after, string title = "휴식 공간",
                                            string subtitle = "잠시 숨을 돌렸다.")
        {
            var canvas = ScreenUi.BuildCanvas("RestCanvas"); // 갈림길과 같은 검은 전체화면
            canvas.sortingOrder = 250;
            var t = ScreenUi.Label(canvas.transform, title, 84f, new Vector2(0, 70));
            t.color = new Color(0.6f, 1f, 0.8f);
            ScreenUi.Label(canvas.transform, subtitle, 36f, new Vector2(0, -20));
            var hp = ScreenUi.Label(canvas.transform, $"HP {before} → {after}", 44f, new Vector2(0, -90));
            hp.color = new Color(0.6f, 1f, 0.75f);
            yield return new WaitForSeconds(2.2f);
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

            var canvas = ScreenUi.BuildCanvas("TreasureCanvas"); // 갈림길과 같은 검은 전체화면
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
