using System.Collections.Generic;
using UnityEngine;

public class OrbitingHead : MonoBehaviour
{
    public Vector3 center;
    public float radius = 3.7f;
    public Vector3 orbitAxis = Vector3.up;
    public float speedDeg = 25f;
    public float angleDeg = 0f;

    public Material hairMat;

    [System.NonSerialized] public List<GameObject> scalpHairs = new List<GameObject>();
    public bool HasHair { get { return scalpHairs.Count > 0; } }

    [System.NonSerialized] public string personName = "Unknown";
    [System.NonSerialized] public int    personAge  = 30;

    // この頭の毛質。地表に積もった毛の太さ・長さに引き継ぐ（基準の髪型に対する倍率）。
    // PlanetRealHeads が髪型を割り当てるときに入れる。未設定なら 1。
    [System.NonSerialized] public float hairThicknessScale = 1f;
    [System.NonSerialized] public float hairLengthScale    = 1f;
    [System.NonSerialized] public float hairCurl           = 1f;   // うねり。0.5 で直毛寄り、2 で癖毛

    Vector3 _b1, _b2;

    readonly List<GameObject> _outlineObjects = new List<GameObject>();
    bool _outlined;

    // ------------------------------------------------------------------
    // 軌道：中心からの向き _up と、それに直交する進行方向 _fwd の二本で持つ。
    // 角度をひとつ進めるだけの円運動だと軌道が固定されてしまい、ぶつかっても
    // 行き先が変えられない。大円運動として持ち直すことで、ぶつかった向きへ
    // 進行方向を折り返せるようになる。半径と速さは変わらないので、頭は
    // いつまでも同じ高さを同じ速さで回り続ける。
    // ------------------------------------------------------------------
    Vector3 _up;    // center から頭へ向かう単位ベクトル
    Vector3 _fwd;   // 進行方向（_up と直交する単位ベクトル）
    bool _orbitReady;

    [Header("頭同士の衝突")]
    [Tooltip("頭の当たり半径。0 以下なら見た目の大きさから自動で決める。")]
    public float collisionRadius = 0f;
    [Tooltip("跳ね返りの強さ。1 で完全に折り返す。")]
    [Range(0f, 1f)] public float bounce = 1f;

    static readonly List<OrbitingHead> _all = new List<OrbitingHead>();
    public static IReadOnlyList<OrbitingHead> All => _all;
    void OnEnable()  { _all.Add(this); }
    void OnDisable() { _all.Remove(this); }

    void Start() { SetupBasis(); EnsureOrbit(); Place(); ResolveCollisions(); }

    void SetupBasis()
    {
        Vector3 a = orbitAxis.sqrMagnitude < 1e-6f ? Vector3.up : orbitAxis.normalized;
        Vector3 t = Mathf.Abs(Vector3.Dot(a, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        _b1 = Vector3.Normalize(Vector3.Cross(a, t));
        _b2 = Vector3.Normalize(Vector3.Cross(a, _b1));
    }

    /// <summary>シーンで設定された軸・角度から、大円運動の状態を作る。</summary>
    void EnsureOrbit()
    {
        if (_orbitReady) return;
        _orbitReady = true;
        if (_b1 == Vector3.zero) SetupBasis();
        float r = angleDeg * Mathf.Deg2Rad;
        _up  = (_b1 * Mathf.Cos(r) + _b2 * Mathf.Sin(r)).normalized;
        _fwd = (-_b1 * Mathf.Sin(r) + _b2 * Mathf.Cos(r)).normalized;
    }

    // 頭の中身は Start のあとに組み上がるので、当たり半径は少し待ってから測る。
    bool _radiusMeasured;
    int  _measureTries;

    void EnsureCollisionRadius()
    {
        if (_radiusMeasured) return;
        if (collisionRadius > 0f && _measureTries == 0) { _radiusMeasured = true; return; }
        if (++_measureTries > 120) { _radiusMeasured = true; if (collisionRadius <= 0f) collisionRadius = 0.12f; return; }
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length < 2) return;        // まだ組み上がっていない
        collisionRadius = MeasureRadius();
        _radiusMeasured = true;
    }

    /// <summary>
    /// 当たり半径を頭蓋の大きさから測る。頭の大きさは個体差がある。
    /// 毛先まで含めた全体の対角で測ると当たりが頭より一回り大きくなり、
    /// 触れていないのに進路が変わって見えるので、頭蓋そのものに合わせる。
    /// </summary>
    float MeasureRadius()
    {
        var skull = transform.Find("Skull");
        var rends = skull != null ? skull.GetComponentsInChildren<Renderer>()
                                  : GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return 0.12f;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        // いちばん張り出している軸の半分＝頭蓋にぴったり外接する球
        float half = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
        if (skull == null) half *= 0.85f;   // 毛を含んだ寸法なので少し詰める
        return Mathf.Clamp(half * 1.02f, 0.08f, 0.25f);
    }

    void Update()
    {
        EnsureOrbit();
        EnsureCollisionRadius();
        Advance(speedDeg * Time.deltaTime);
        Place();
    }

    // 衝突は全頭が進み終えてから解く。Update の中で解くと、あとから進んだ頭が
    // 解決済みの重なりを作り直してしまい、次のフレームまで食い込んだままになる。
    void LateUpdate()
    {
        if (!_orbitReady) return;
        ResolveCollisions();
    }

    /// <summary>大円に沿って deg 度進む。向きの対（_up, _fwd）をまとめて回す。</summary>
    void Advance(float deg)
    {
        Vector3 axis = Vector3.Cross(_up, _fwd);
        if (axis.sqrMagnitude < 1e-8f) return;
        var q = Quaternion.AngleAxis(deg, axis.normalized);
        _up  = (q * _up).normalized;
        _fwd = Vector3.ProjectOnPlane(q * _fwd, _up).normalized;
        angleDeg += deg;   // 外から見た通し角度。表示や記録用に残す。
    }

    void Place()
    {
        Vector3 pos = center + _up * radius;
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(_fwd, _up);
    }

    /// <summary>
    /// 他の頭と重なっていたら、ぶつかった向きへ跳ね返して離す。
    /// 近づいている組だけを扱う（離れていく組まで折り返すと、へばりついてしまう）。
    /// </summary>
    void ResolveCollisions()
    {
        // 各組を一度だけ扱う。自分より後ろにいる頭だけを見る。
        int me = _all.IndexOf(this);
        if (me < 0) return;
        for (int i = me + 1; i < _all.Count; i++)
        {
            var other = _all[i];
            if (other == null || !other._orbitReady) continue;

            Vector3 d = other.transform.position - transform.position;
            float dist = d.magnitude;
            float min = collisionRadius + other.collisionRadius;
            if (dist >= min || dist < 1e-5f) continue;

            Vector3 n = d / dist;   // this → other

            // それぞれの球面上での「離れたい向き」を作る。
            // 高さ違いの頭が真上・真下で重なると、球面に沿った逃げ場が無くなるので、
            // そのときは二頭が左右へすれ違うように、共通の横向きを与える。
            Vector3 awayA = Vector3.ProjectOnPlane(-n, _up);
            Vector3 awayB = Vector3.ProjectOnPlane( n, other._up);
            if (awayA.sqrMagnitude < 1e-6f || awayB.sqrMagnitude < 1e-6f)
            {
                Vector3 lat = Vector3.Cross(n, _up);
                if (lat.sqrMagnitude < 1e-8f) lat = Vector3.Cross(n, _fwd);
                if (lat.sqrMagnitude < 1e-8f) lat = Vector3.Cross(n, Vector3.up);
                if (lat.sqrMagnitude < 1e-8f) continue;
                lat.Normalize();
                awayA = Vector3.ProjectOnPlane( lat, _up);
                awayB = Vector3.ProjectOnPlane(-lat, other._up);
            }
            if (awayA.sqrMagnitude < 1e-8f || awayB.sqrMagnitude < 1e-8f) continue;
            awayA.Normalize(); awayB.Normalize();

            // 近づいているときだけ跳ね返す（離れていく組まで折り返すと貼り付く）
            Vector3 vA = _fwd * (speedDeg * Mathf.Deg2Rad * radius);
            Vector3 vB = other._fwd * (other.speedDeg * Mathf.Deg2Rad * other.radius);
            if (Vector3.Dot(vB - vA, n) < 0f)
            {
                Deflect(awayA, bounce);
                other.Deflect(awayB, other.bounce);
            }

            // 重なったぶんを、それぞれの球面に沿って押し戻す。
            // 高さの差（半径方向のずれ）は球面上では詰められないので、そのぶんを除いて、
            // 横方向にどれだけ離れれば触れなくなるかを出す。重なりの量をそのまま横へ
            // 押すと、真上・真下で重なった組がいつまでも離れない。
            Vector3 upAvg = (_up + other._up).normalized;
            float radialGap = Mathf.Abs(Vector3.Dot(d, upAvg));
            float lateralNow  = Vector3.ProjectOnPlane(d, upAvg).magnitude;
            float lateralNeed = Mathf.Sqrt(Mathf.Max(min * min - radialGap * radialGap, 0f));
            float push = (lateralNeed - lateralNow) * 0.5f + 1e-4f;
            if (push <= 0f) continue;

            Separate(awayA, push);
            other.Separate(awayB, push);
        }
    }

    /// <summary>
    /// 進行方向を away（球面に沿った離れたい向き）へ折り返す。
    /// 速さも半径も変えず、向きだけを変える。
    /// </summary>
    void Deflect(Vector3 away, float amount)
    {
        float into = -Vector3.Dot(_fwd, away);   // 相手へ突っ込んでいる量
        if (into <= 0f) return;                  // すでに離れる向き
        Vector3 reflected = _fwd + away * (into * (1f + amount));
        if (reflected.sqrMagnitude < 1e-6f) reflected = away;
        _fwd = Vector3.ProjectOnPlane(reflected, _up).normalized;
    }

    /// <summary>球面に沿って away の向きへ dist だけずらす（半径は保つ）。</summary>
    void Separate(Vector3 away, float dist)
    {
        if (radius < 1e-5f) return;
        Vector3 axis = Vector3.Cross(_up, away);
        if (axis.sqrMagnitude < 1e-8f) return;
        float deg = dist / radius * Mathf.Rad2Deg;
        var q = Quaternion.AngleAxis(deg, axis.normalized);
        _up  = (q * _up).normalized;
        _fwd = Vector3.ProjectOnPlane(q * _fwd, _up).normalized;
        transform.position = center + _up * radius;
        transform.rotation = Quaternion.LookRotation(_fwd, _up);
    }

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

    public void ShowOutline(Material outlineMat)
    {
        if (_outlined) return;
        _outlined = true;
        var skull = transform.Find("Skull");
        if (skull == null) return;
        var mf = skull.GetComponent<MeshFilter>();
        if (mf == null) return;
        var twin = new GameObject("_Outline");
        twin.transform.SetParent(skull, false);
        twin.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var mr = twin.AddComponent<MeshRenderer>();
        mr.sharedMaterial = outlineMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        _outlineObjects.Add(twin);
    }

    public void HideOutline()
    {
        if (!_outlined) return;
        _outlined = false;
        foreach (var go in _outlineObjects)
            if (go != null) Destroy(go);
        _outlineObjects.Clear();
    }
}
