using UnityEngine;

/// <summary>
/// 頭から落ちる一本の細い黒毛（円柱ベース、少し艶あり）。地球に影を落とす。
/// 落下：地表付近まで半径を縮めつつ横へ流れて曲がる。
/// 着地後：地表すぐ上をゆっくり漂う。PlanetController が生成・設定する。
/// </summary>
public class PlanetHair : MonoBehaviour
{
    Vector3 _center;
    float _landRadius;
    Vector3 _dir;
    float _radius;
    float _fallSpeed;
    float _driftDeg;
    Vector3 _driftAxis;
    bool _landed;
    float _bobPhase;
    float _spin;
    float _spinSpeed;

    public void Init(Vector3 center, float landRadius, Vector3 startPos, Material mat,
                     float thickness, float length, float fallSpeed, float driftDegPerSec, Vector3 driftAxis)
    {
        _center = center;
        _landRadius = landRadius;
        Vector3 rel = startPos - center;
        _radius = rel.magnitude;
        _dir = rel.sqrMagnitude < 1e-6f ? Vector3.up : rel.normalized;
        _fallSpeed = fallSpeed;
        _driftDeg = driftDegPerSec;
        _driftAxis = driftAxis.sqrMagnitude < 1e-6f ? Vector3.up : driftAxis.normalized;
        _bobPhase = Random.Range(0f, 6.28318f);
        _spin = Random.Range(0f, 360f);
        _spinSpeed = Random.Range(-25f, 25f);
        BuildStrand(gameObject, length, thickness, mat);
        PlaceInternal(_radius);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        _dir = (Quaternion.AngleAxis(_driftDeg * dt, _driftAxis) * _dir).normalized;

        if (!_landed)
        {
            _radius = Mathf.MoveTowards(_radius, _landRadius, _fallSpeed * dt);
            if (_radius <= _landRadius + 1e-3f) _landed = true;
        }

        float rad = _landed
            ? _landRadius + Mathf.Sin(Time.time * 0.6f + _bobPhase) * 0.05f
            : _radius;

        _spin += _spinSpeed * dt;
        PlaceInternal(rad);
    }

    void PlaceInternal(float rad)
    {
        transform.position = _center + _dir * rad;
        Vector3 tangent = Vector3.Cross(_dir, _driftAxis);
        if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.Cross(_dir, Vector3.up);
        tangent.Normalize();
        Quaternion look = Quaternion.LookRotation(tangent, _dir);
        transform.rotation = look * Quaternion.AngleAxis(_spin, Vector3.forward);
    }

    /// <summary>
    /// 局所 Z 軸に沿った、ゆるく曲がる細い毛を円柱で作る（元の質感）。影も落とす。
    /// go の子として円柱セグメントを生成する（頭皮の毛・落ちる毛の両方で使う）。
    /// </summary>
    public static void BuildStrand(GameObject go, float length, float thickness, Material mat)
    {
        int seg = 3;
        Vector3 prev = PointOnStrand(0f, length);
        for (int i = 1; i <= seg; i++)
        {
            Vector3 p = PointOnStrand((float)i / seg, length);
            MakeSegment(go.transform, prev, p, thickness, mat);
            prev = p;
        }
    }

    static Vector3 PointOnStrand(float t, float length)
    {
        float z = (t - 0.5f) * length;
        float x = Mathf.Sin(t * Mathf.PI * 1.5f) * length * 0.12f;
        return new Vector3(x, 0f, z);
    }

    static void MakeSegment(Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.transform.SetParent(parent, false);
        Vector3 dir = b - a;
        float len = dir.magnitude;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        go.transform.localScale = new Vector3(thickness, len * 0.5f, thickness);
        var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // 地球に影を落とす
    }
}
