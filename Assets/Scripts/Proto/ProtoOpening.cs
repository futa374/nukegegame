using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// オープニングの後半（CG側）。
///
/// 前半（街・人・頭から毛が離れる・床へ落ちる）は実写で撮る。
/// 実写の最後で背景が暗転しきったところから、この場面が引き継ぐ。
/// だから、この場面は「ほぼ真っ黒な画面に毛が一本だけ大きく写っている」状態から始まる。
/// 実写の最終フレームと、ここの最初のフレームが同じ絵になるように作る。
///
/// ここでやることは一つだけ。カメラを引くこと。
/// 毛は動かさず、周りが退いていく。やがて下に地球が現れ、
/// 同じように降ってくる他の毛と、毛を落とし続ける頭たちが画角に入る。
/// 引き終わったところで、この毛自身も落下を始めて地表の一本目になる。
///
/// 毛を上へ飛ばして宇宙へ向かわせると、本編（毛が地球へ降り積もる）と
/// 向きが逆になる。ここでは毛は動かさず、カメラだけが退がる。
/// </summary>
public class ProtoOpening : MonoBehaviour
{
    [Header("再生")]
    public bool playOnStart = true;
    [Tooltip("引き切るまでの秒数")]
    public float duration = 16f;
    [Tooltip("始める前に、世界が組み上がるのを待つフレーム数。"
           + "読み込みの引っかかりの最中に始めると、そのぶん頭出しが飛ぶ。")]
    public int warmupFrames = 30;
    [Tooltip("押すと途中で飛ばせる")]
    public Key skipKey = Key.Escape;

    [Header("カメラ")]
    [Tooltip("始まりの、毛までの距離。毛（長さ0.22）が画面いっぱいに写る距離。")]
    public float startDistance = 0.26f;
    [Tooltip("引き終わりの、地球の中心までの距離。未設定ならカメラ側の値を使う。")]
    public float endDistance = 0f;
    [Tooltip("引きの速さの配り方。最初はゆっくり、後半で一気に退がる。")]
    public AnimationCurve ease = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0.15f), new Keyframe(0.55f, 0.28f), new Keyframe(1f, 1f, 2.2f, 0f));

    [Header("毛")]
    [Tooltip("毛を置く高さ（地球の中心からの距離）。1.0が地表。")]
    public float hairRadius = 1.52f;
    public float hairLength = 0.22f;
    [Tooltip("毛の太さ。ここまで寄ると本編の太さでは棒に見えるので、細くする。")]
    public float hairThickness = 0.0042f;
    [Tooltip("芯線の分割数。本編は4分割で、寄ると折れ線に見える。")]
    [Range(6, 48)] public int hairSegments = 26;
    [Tooltip("毛先へ向かってどこまで細くするか")]
    [Range(0.05f, 1f)] public float tipTaper = 0.22f;
    [Tooltip("毛がゆっくり漂う速さ（度/秒）")]
    public float driftDegPerSec = 2.2f;
    [Tooltip("オープニングの毛の色。本編の毛は真っ黒に近く、宇宙を背景にすると消えてしまう。"
           + "ここだけは背景から浮く明るさにする。")]
    public Color hairColor = new Color(0.26f, 0.20f, 0.17f);
    [Tooltip("毛の自発光。暗い画面でも輪郭が残るように、ごく弱く足す。")]
    [Range(0f, 1f)] public float hairGlow = 0.16f;

    [Header("本編へ渡す")]
    [Tooltip("この割合を過ぎたら、他の毛も降り始める。早すぎると画面が散らかる。")]
    [Range(0f, 1f)] public float shedStartsAt = 0.55f;

    // 差し替えて元に戻すもの
    PlanetCameraRig _rig;
    PlanetController _pc;
    ProtoJourneyLog _log;
    float _savedShedInterval = -1f;
    bool _savedLogHidden;

    Transform _hair;
    Vector3 _hairDir;          // 地球の中心から毛へ向かう向き
    Vector3 _driftAxis;
    float _t;
    bool _running, _done;
    bool _shedResumed;

    Vector3 _endPos;
    Quaternion _endRot;

    public bool IsPlaying => _running;

    bool _armed;
    int _smooth;

    void Start()
    {
        if (playOnStart) _armed = true;
    }

    public void Begin()
    {
        var cam = Camera.main;
        if (cam == null) { _done = true; return; }

        _rig = cam.GetComponent<PlanetCameraRig>();
        _pc  = FindAnyObjectByType<PlanetController>();
        _log = FindAnyObjectByType<ProtoJourneyLog>();

        // 引き終わりの画＝本編の既定の画。ここへ着地させれば、そのまま操作に移れる。
        float d = endDistance > 0f ? endDistance : (_rig != null ? _rig.distance : 3.3f);
        Vector3 pivot = _rig != null ? _rig.pivot : Vector3.zero;
        float az = (_rig != null ? _rig.azimuth : 180f) * Mathf.Deg2Rad;
        float el = (_rig != null ? _rig.elevation : 8f) * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        _endPos = pivot + dir * d;
        _endRot = Quaternion.LookRotation(pivot - _endPos, Vector3.up);

        // 毛は、引き終わりのカメラから見て手前寄りの空に置く。
        // 真正面に置くと、引いたときに地球のど真ん中へ重なって見失う。
        Vector3 toCam = (_endPos - pivot).normalized;
        Vector3 side  = Vector3.Cross(toCam, Vector3.up).normalized;
        _hairDir = (toCam * 0.72f + side * 0.42f + Vector3.up * 0.30f).normalized;
        _driftAxis = Vector3.Cross(_hairDir, Vector3.up).normalized;

        BuildHair();

        // 前半は毛を一本だけ見せたい。他の毛が降り始めるのは後半から。
        if (_pc != null) { _savedShedInterval = _pc.shedInterval; _pc.shedInterval = 9999f; }
        if (_log != null) { _savedLogHidden = _log.hidden; _log.hidden = true; }
        if (_rig != null) _rig.enabled = false;

        _t = 0f; _running = true; _done = false; _shedResumed = false;
        Apply(0f);
    }

    void BuildHair()
    {
        var go = new GameObject("OpeningHair");
        _hair = go.transform;
        _hair.position = _hairDir * hairRadius;

        // 本編の毛は真っ黒に近い。地球を背景にしているから見えているだけで、
        // 宇宙に置くと背景に溶けて消える。この一本だけは自前の材質にする。
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hairColor);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", hairColor);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.42f);
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);
        if (hairGlow > 0f && mat.HasProperty("_EmissionColor"))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", hairColor * hairGlow);
        }
        BuildSmoothStrand(go, mat);
    }

    /// <summary>
    /// 寄りに耐える一本を作る。本編の毛は4分割の円柱で、遠目には毛に見えるが、
    /// 画面いっぱいに写すと折れ線の棒になる。分割を増やし、毛先へ向けて細める。
    /// 形（うねり方）は本編と同じ式から取るので、この一本だけ違う形にはならない。
    /// </summary>
    void BuildSmoothStrand(GameObject go, Material mat)
    {
        int seg = Mathf.Clamp(hairSegments, 6, 48);
        Vector3 prev = PlanetHair.PointOnStrand(0f, hairLength, 1f);
        for (int i = 1; i <= seg; i++)
        {
            Vector3 p = PlanetHair.PointOnStrand((float)i / seg, hairLength, 1f);
            float tm = ((float)i - 0.5f) / seg;
            float th = hairThickness * Mathf.Lerp(1f, tipTaper, tm * tm);
            var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            c.transform.SetParent(go.transform, false);
            Vector3 d = p - prev;
            c.transform.localPosition = (prev + p) * 0.5f;
            c.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            c.transform.localScale = new Vector3(th, d.magnitude * 0.5f, th);
            var col = c.GetComponent<Collider>(); if (col != null) Destroy(col);
            var mr = c.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            prev = p;
        }
    }

    void Update()
    {
        // 世界が組み上がるまで待つ。引っかかっている間は数えない。
        if (_armed && !_running && !_done)
        {
            _smooth = Time.deltaTime < 0.05f ? _smooth + 1 : 0;
            if (_smooth >= warmupFrames) { _armed = false; Begin(); }
            return;
        }
        if (!_running) return;
        var kb = Keyboard.current;
        if (kb != null && kb[skipKey].wasPressedThisFrame) { Finish(); return; }

        // 一瞬の引っかかりで場面が飛ばないよう、進み方の上限を決めておく
        _t += Mathf.Min(Time.deltaTime, 0.05f) / Mathf.Max(0.1f, duration);
        if (_t >= 1f) { Apply(1f); Finish(); return; }
        Apply(_t);
    }

    void Apply(float t)
    {
        float s = Mathf.Clamp01(ease.Evaluate(t));

        // 毛はほとんど動かない。ごくゆっくり漂うだけ。
        _hairDir = (Quaternion.AngleAxis(driftDegPerSec * Time.deltaTime, _driftAxis) * _hairDir).normalized;
        Vector3 hp = _hairDir * hairRadius;
        if (_hair != null)
        {
            _hair.position = hp;
            Vector3 up = hp.normalized;
            Vector3 fwd = Vector3.Cross(up, _driftAxis);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.Cross(up, Vector3.right);
            _hair.rotation = Quaternion.LookRotation(fwd.normalized, up);
        }

        // 見る先が、毛から地球の中心へ移っていく。
        Vector3 look = Vector3.Lerp(hp, Vector3.zero, s * s);

        // 始まりのカメラは、地球と毛のあいだに置いて外を向く。
        // 毛の外側から見ると毛の向こうに地球が写り込んでしまい、
        // 実写から渡ってくる「ほぼ真っ黒な画面」にならない。
        Vector3 near = hp - hp.normalized * startDistance;
        Vector3 pos = Vector3.Lerp(near, _endPos, s);

        var cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = pos;
        cam.transform.rotation = Quaternion.Slerp(
            Quaternion.LookRotation(hp - pos, Vector3.up),
            Quaternion.LookRotation(look - pos, Vector3.up), s);

        // 後半に入ったら、他の毛も降り始める。
        if (!_shedResumed && t >= shedStartsAt)
        {
            _shedResumed = true;
            if (_pc != null && _savedShedInterval > 0f) _pc.shedInterval = _savedShedInterval;
        }
    }

    /// <summary>本編へ渡す。カメラの操作を返し、この一本も落下を始めさせる。</summary>
    void Finish()
    {
        if (_done) return;
        _done = true; _running = false;

        var cam = Camera.main;
        if (cam != null) { cam.transform.position = _endPos; cam.transform.rotation = _endRot; }
        if (_rig != null) _rig.enabled = true;
        if (_pc != null && _savedShedInterval > 0f) _pc.shedInterval = _savedShedInterval;
        if (_log != null) _log.hidden = _savedLogHidden;

        // この毛を、地表へ降る一本目として落とす。
        // ここで消してしまうと、見せてきた毛がどこへ行ったのか分からなくなる。
        if (_hair != null) ReleaseHair();
    }

    void ReleaseHair()
    {
        var overlay = FindAnyObjectByType<ProtoFieldOverlay>();
        var go = _hair.gameObject;
        _hair = null;
        if (overlay == null) { Destroy(go, 2f); return; }

        var ph = go.AddComponent<PlanetHair>();
        ph.ownerName = "（最初の一本）";
        ph.birthTimeString = GameClock.Instance != null ? GameClock.Instance.TimeString : "";
        // Init を通さずに足しているので、地表へ積もるときに読む形だけ自分で入れる
        ph.strandThickness = hairThickness;
        ph.strandLength    = hairLength;
        ph.strandCurl      = 1f;
        overlay.AdoptHair(ph);
    }
}
