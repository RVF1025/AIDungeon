using System.Collections;
using UnityEngine;

namespace AIDungeon.Director
{
    /// <summary>
    /// 왕복 검증용 최소 테스트러너. 빈 GameObject에 이 스크립트 + GeminiDirectorClient를 붙이고
    /// Play를 누르면, 미리 정의된 프로파일들로 순차 요청해 Console에 결과를 찍는다.
    /// WebGL 빌드에서도 동일하게 동작(프록시 왕복·폴백 확인용).
    /// </summary>
    [RequireComponent(typeof(GeminiDirectorClient))]
    public class AIDirectorTester : MonoBehaviour
    {
        private GeminiDirectorClient _client;

        // 스타일이 뚜렷이 갈리는 대표 프로파일들.
        private readonly PlayerProfile[] _samples =
        {
            new PlayerProfile(0.90f, 0.70f, 0.90f), // 근접·저돌·압도 → kiter/open, taunt 기대
            new PlayerProfile(0.10f, 0.30f, 0.25f), // 원거리·회피·빈사 → rusher/cover?, concern 기대
            new PlayerProfile(0.50f, 0.90f, 0.60f), // 균형·저돌 → tank_bait/open
            new PlayerProfile(0.05f, 0.50f, 0.95f), // 순수원거리·압도 → rusher, impressed 기대
        };

        private void Awake() => _client = GetComponent<GeminiDirectorClient>();

        private IEnumerator Start()
        {
            Debug.Log("[AIDirector] 테스트 시작 — 프록시 왕복 검증");
            for (int i = 0; i < _samples.Length; i++)
            {
                var p = _samples[i];
                Debug.Log($"→ 요청: {p.ToPromptLine()}");
                yield return _client.RequestDecision(p, i + 1, "", "", d =>
                {
                    Debug.Log($"← 결과: {d}");
                });
                yield return new WaitForSeconds(0.5f);
            }
            Debug.Log("[AIDirector] 테스트 완료");
        }

        // 인스펙터 우클릭 > TestFallback 으로 오프라인 폴백만 즉시 확인.
        [ContextMenu("TestFallback")]
        private void TestFallback()
        {
            foreach (var p in _samples)
                Debug.Log($"[FALLBACK] {p.ToPromptLine()}\n  {FallbackPresets.Build(p)}");
        }
    }
}
