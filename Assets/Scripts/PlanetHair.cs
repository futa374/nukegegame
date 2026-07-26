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

    public string ownerName      = "";
    public string birthTimeString = "";

    readonly System.Collections.Generic.List<Renderer> _renderers = new System.Collections.Generic.List<Renderer>();
    readonly System.Collections.Generic.List<GameObject> _outlineObjects = new System.Collections.Generic.List<GameObject>();
    bool _outlined;
    bool _tangled;
    int  _hairCheckTimer;

    // DustParticleが毛を探すための静的リスト
    static readonly System.Collections.Generic.List<PlanetHair> _all = new System.Collections.Generic.List<PlanetHair>();
    public static System.Collections.Generic.IReadOnlyList<PlanetHair> All => _all;

    void OnEnable()  { _all.Add(this); }
    void OnDisable() { _all.Remove(this); }
    void OnDestroy() { _all.Remove(this); }

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
        GetComponentsInChildren<Renderer>(_renderers);

        // ホバー検出用コライダー（ストランドのローカルZ軸方向に沿ったカプセル）
        var col = gameObject.AddComponent<CapsuleCollider>();
        col.direction = 2; // Z軸
        col.height = length * 1.3f;
        col.radius = Mathf.Max(thickness * 5f, 0.018f);

        PlaceInternal(_radius);
    }

    public void ShowOutline(Material outlineMat)
    {
        if (_outlined) return;
        _outlined = true;
        foreach (var r in _renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null) continue;
            var twin = new GameObject("_Outline");
            twin.transform.SetParent(r.transform, false);
            twin.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = twin.AddComponent<MeshRenderer>();
            mr.sharedMaterial = outlineMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _outlineObjects.Add(twin);
        }
    }

    public void HideOutline()
    {
        if (!_outlined) return;
        _outlined = false;
        foreach (var go in _outlineObjects)
            if (go != null) Destroy(go);
        _outlineObjects.Clear();
    }

    void Update()
    {
        if (_tangled) return;

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

        // 毛同士の絡まりチェック（15フレームに1回）
        if (++_hairCheckTimer < 15) return;
        _hairCheckTimer = 0;
        for (int i = 0; i < _all.Count; i++)
        {
            var other = _all[i];
            if (other == null || other == this || other._tangled) continue;
            if (Vector3.Distance(transform.position, other.transform.position) < 0.06f)
            {
                TangleWith(other);
                return;
            }
        }
    }

    void TangleWith(PlanetHair other)
    {
        _tangled = true;
        transform.SetParent(other.transform, false);
        transform.localPosition = new Vector3(
            Random.Range(-0.015f, 0.015f),
            Random.Range(-0.015f, 0.015f),
            Random.Range(-0.030f, 0.030f));
        transform.localRotation = Quaternion.Euler(
            Random.Range(-60f, 60f), Random.Range(0f, 360f), Random.Range(-30f, 30f));
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
