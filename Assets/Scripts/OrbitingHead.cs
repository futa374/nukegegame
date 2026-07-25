using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地球(center)の周りを、ひとつの大円に沿って旋回する頭。顔(+Z)は進行方向を向く。
/// 頭には毛(scalpHairs)が生えており、抜けるほど（RemoveOneHair）ハゲていく。
/// 頭ごとに毛の色(hairMat)と形(hairStyle)を持ち、落ちる毛もそれに揃える。
/// PlanetController が生成・設定する。
/// </summary>
public class OrbitingHead : MonoBehaviour
{
    public Vector3 center;
    public float radius = 3.7f;
    public Vector3 orbitAxis = Vector3.up;
    public float speedDeg = 25f;
    public float angleDeg = 0f;

    // 毛（頭と落ちる毛で共有）
    public Material hairMat;

    [System.NonSerialized] public List<GameObject> scalpHairs = new List<GameObject>();
    public bool HasHair { get { return scalpHairs.Count > 0; } }

    Vector3 _b1, _b2;

    void Start() { SetupBasis(); Place(); }

    void SetupBasis()
    {
        Vector3 a = orbitAxis.sqrMagnitude < 1e-6f ? Vector3.up : orbitAxis.normalized;
        Vector3 t = Mathf.Abs(Vector3.Dot(a, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        _b1 = Vector3.Normalize(Vector3.Cross(a, t));
        _b2 = Vector3.Normalize(Vector3.Cross(a, _b1));
    }

    void Update()
    {
        angleDeg += speedDeg * Time.deltaTime;
        Place();
    }

    void Place()
    {
        float r = angleDeg * Mathf.Deg2Rad;
        Vector3 pos = center + (_b1 * Mathf.Cos(r) + _b2 * Mathf.Sin(r)) * radius;
        Vector3 vel = (-_b1 * Mathf.Sin(r) + _b2 * Mathf.Cos(r)).normalized;
        Vector3 up = (pos - center).normalized;
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(vel, up);
    }

    /// <summary>頭の毛を一本抜く。抜けたら true、もうハゲなら false。</summary>
    public bool RemoveOneHair()
    {
        while (scalpHairs.Count > 0)
        {
            int i = scalpHairs.Count - 1;
            var g = scalpHairs[i];
            scalpHairs.RemoveAt(i);
            if (g != null) { Destroy(g); return true; }
        }
        return false;
    }
}
