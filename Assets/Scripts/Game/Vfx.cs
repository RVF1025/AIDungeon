using TMPro;
using UnityEngine;

namespace AIDungeon.Game
{
    /// <summary>코드로 생성하는 전투 이펙트(데미지 숫자/파열/스파크). 에셋 불필요.</summary>
    public static class Vfx
    {
        public static void DamageNumber(Vector3 pos, float amount, Color color)
        {
            var go = new GameObject("Dmg");
            go.transform.position = pos + Vector3.up * 0.4f + (Vector3)(Random.insideUnitCircle * 0.25f);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = Mathf.RoundToInt(amount).ToString();
            tmp.fontSize = 4.5f;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = 25;
            var fx = go.AddComponent<FxLife>().Bind();
            fx.velocity = new Vector3(Random.Range(-0.5f, 0.5f), 2.2f, 0);
            fx.life = 0.6f;
        }

        /// <summary>사망 파열: 사방으로 튀는 작은 원 조각들.</summary>
        public static void Burst(Vector3 pos, Color color, int pieces = 8, float speed = 6f)
        {
            for (int i = 0; i < pieces; i++)
            {
                float ang = (360f / pieces) * i * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0);
                var go = new GameObject("Frag");
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * 0.28f;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFactory.Circle();
                sr.color = color;
                sr.sortingOrder = 15;
                var fx = go.AddComponent<FxLife>().Bind();
                fx.velocity = dir * (speed * Random.Range(0.6f, 1.1f));
                fx.scalePerSec = -0.5f;
                fx.life = Random.Range(0.25f, 0.4f);
            }
        }

        /// <summary>피격 스파크: 짧게 번쩍하는 원.</summary>
        public static void Spark(Vector3 pos, Color color)
        {
            var go = new GameObject("Spark");
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.5f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Circle();
            sr.color = color;
            sr.sortingOrder = 14;
            var fx = go.AddComponent<FxLife>().Bind();
            fx.scalePerSec = 3f;
            fx.life = 0.12f;
        }
    }
}
