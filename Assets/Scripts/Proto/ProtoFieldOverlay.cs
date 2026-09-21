using UnityEngine;

/// <summary>
/// 既存の planet 系シーンに、静電気のもやと旅路レポートを後から重ねる。
///
/// 置くだけで効く。シーンの作り（PlanetController が頭と毛を作り、PlanetRealHeads が
/// 髪型を整え、HairHoverController がクリックで追う）には手を入れない。
/// 新しく抜けた毛を見つけたら、落下だけを速度ベースへ差し替える。
///
/// 入力は既存と衝突しないように分けてある。
///   左クリック・左ドラッグ … これまで通り（毛や頭の選択、視点回転）
///   右クリック単押し       … もやの点を置く
///   Enter / Esc            … 引き終わる
///   C                      … もやを消す
/// </summary>
[DefaultExecutionOrder(400)]     // PlanetController(既定) と PlanetRealHeads(200) の後
public class ProtoFieldOverlay : MonoBehaviour
{
    [Header("落下")]
    // シーンによって星の大きさも周回高度も違う。数値を直に置くと、あるシーンでは
    // ふわりと落ち、別のシーンでは一瞬で着いてしまう。落ちるのにかけたい秒数を決めて、
    // 実際の落下距離から逆算する。
    // 長くすれば先回りして構造を組む時間は増えるが、落ちているように見えなくなる。
    // このシーンは落下距離が星の半径の 1 割ほどしかないので、20 秒もかけると
    // 毎秒 0.6% しか動かず、宙に止まった群れに見えてしまった。
    // 落ちる姿が読める速さを優先して短くしてある。
    [Tooltip("頭の高さから地表まで、何秒かけて落ちるか。")]
    public float fallSeconds = 7f;
    // 抜けた毛は、頭が動いていた速さと向きをそのまま持って離れる。慣性。
    // 空気抵抗がそれを削っていき、重力が沈めていく。頭の周回速度は落下の終端速度の
    // 8 倍ほどあるので、しばらくは頭の進行方向へ流れ、やがて弧を描いて沈む。
    // 抵抗を強くすると、頭の速さが一瞬で消えて真下へ落ちるように見えてしまう。
    [Tooltip("空気抵抗。小さいほど頭の勢いが長く残り、弧が大きくなる。")]
    public float drag = 0.7f;
    [Tooltip("抜けた瞬間に、頭の速度をどれだけ引き継ぐか。1 でそのまま。")]
    [Range(0f, 1.5f)] public float inheritHeadVelocity = 1.0f;
    [Tooltip("引き継ぎに加える、ばらけ。0 で全部同じ向きに飛ぶ。")]
    [Range(0f, 1f)] public float releaseScatter = 0.15f;

    [Header("着地")]
    // PlanetController は毛に「着地＝地球半径＋0.12」を渡している。元の PlanetHair が
    // 地表すぐ上を漂う設計だったための余白だが、蓄積されたあともその高さに残るので、
    // 大気圏で止まって見える。ここでは地表そのものに着ける。
    [Tooltip("地表からどれだけ浮かせて着地させるか。毛の太さぶんだけ。")]
    public float landClearance = 0.01f;

    [Header("もや")]
    [Tooltip("点を置く球殻の半径。0 なら地球と頭の周回高度の間に自動で置く。")]
    public float shellRadius = 0f;
    [Range(0f, 1f)] public float shellBetween = 0.5f;
    [Tooltip("吸着範囲や帯の太さも、落下距離に対する割合で決める。0 で自動。")]
    public float attractRadiusRatio = 0.23f;

    float gravity;   // fallSeconds と drag から導く

    Transform _globe;
    Vector3   _center;
    float     _landRadius, _fallDistance;
    StaticLineField _field;

    // 頭の速度。位置の差分から毎フレーム求める。
    // OrbitingHead の内部の基底ベクトルは非公開なので、動きそのものを観測する。
    readonly System.Collections.Generic.Dictionary<OrbitingHead, Vector3> _headPrev
        = new System.Collections.Generic.Dictionary<OrbitingHead, Vector3>();
    readonly System.Collections.Generic.Dictionary<OrbitingHead, Vector3> _headVel
        = new System.Collections.Generic.Dictionary<OrbitingHead, Vector3>();
    OrbitingHead[] _heads = System.Array.Empty<OrbitingHead>();
    float _headScanTimer;

    void Start()
    {
        var pc = FindAnyObjectByType<PlanetController>();
        var spin = FindAnyObjectByType<EarthSpin>();
        _globe = spin != null ? spin.transform : null;

        _center = pc != null ? pc.transform.position : Vector3.zero;
        float globeR = pc != null ? pc.globeRadius : 3.5f;
        float orbitR = globeR + (pc != null ? pc.orbitHeight : 1.5f);
        // 毛が積もる高さ。地表そのもの。
        _landRadius = globeR + landClearance;

        // 地球にコライダーを持たせる。無いとクリックのレイが星を素通りして、
        // 見えていない裏側の毛に当たり、そちらへカメラが飛んでいく。
        // 星の面でレイを止めれば、見えているものだけが選べる。
        if (_globe != null && _globe.GetComponent<Collider>() == null)
        {
            var sc = _globe.gameObject.AddComponent<SphereCollider>();
            float s = Mathf.Max(_globe.lossyScale.x, 1e-4f);
            sc.radius = globeR / s;                 // ローカル半径へ
            sc.center = Vector3.zero;
        }

        // 落下距離。ここを基準に、速さも吸着範囲も決める。
        _fallDistance = Mathf.Max(orbitR - _landRadius, 1e-4f);
        float terminal = _fallDistance / Mathf.Max(fallSeconds, 0.1f);
        gravity = terminal * drag;

        var lineGo = new GameObject("StaticLineField");
        lineGo.transform.SetParent(transform, false);
        _field = lineGo.AddComponent<StaticLineField>();
        _field.center = _center;
        // 殻は「毛が落ちてくる通り道」の中に置く。地表から測ると着地高度すれすれになり、
        // 毛が通り過ぎる一瞬しか掛からない。
        _field.shellRadius = shellRadius > 0f ? shellRadius : Mathf.Lerp(_landRadius, orbitR, shellBetween);
        _field.ProtoGravityHint = gravity;
        _field.targetCamera = Camera.main;

        // 吸着まわりは、落下距離に対する割合で揃える。proto1 で合わせた比率をそのまま使う。
        float d = _fallDistance;
        _field.attractRadius       = d * attractRadiusRatio;
        _field.holdRadius          = d * 0.11f;
        _field.cohesionRadius      = d * 0.13f;
        _field.cohesionMinDistance = d * 0.025f;
        _field.hazeWidth           = d * 0.13f;
        // 力は重力と釣り合う大きさに。proto1 では重力 0.25 に対して吸着 0.9 だった。
        _field.attractStrength = gravity * 3.6f;
        _field.slideStrength   = gravity * 0.64f;

        var logGo = new GameObject("ProtoJourneyLog");
        logGo.transform.SetParent(transform, false);
        var log = logGo.AddComponent<ProtoJourneyLog>();
        if (_globe != null) log.SetGlobe(_globe);

        // 右クリック単押しでもやの点を置く。既存カメラは左ドラッグなので衝突しない。
        var picker = gameObject.AddComponent<ProtoRightClickPlacer>();
        picker.field = _field;
    }

    void Update()
    {
        TrackHeads();

        // 新しく抜けた毛を拾って、落下を速度ベースへ差し替える。
        var all = PlanetHair.All;
        for (int i = 0; i < all.Count; i++)
        {
            var h = all[i];
            if (h == null || h.drivenExternally) continue;
            if (h.GetComponent<FieldHair>() != null) continue;
            // 頭皮に生えている毛は PlanetHair を持たない。ここに来るのは抜けた毛だけ。
            Attach(h);
        }
    }

    /// <summary>頭の位置を毎フレーム控えて、差分から速度を出す。</summary>
    void TrackHeads()
    {
        _headScanTimer -= Time.deltaTime;
        if (_headScanTimer <= 0f)
        {
            _headScanTimer = 1f;
            _heads = FindObjectsByType<OrbitingHead>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        foreach (var hd in _heads)
        {
            if (hd == null) continue;
            Vector3 p = hd.transform.position;
            if (_headPrev.TryGetValue(hd, out Vector3 prev))
                _headVel[hd] = (p - prev) / dt;
            _headPrev[hd] = p;
        }
    }

    /// <summary>
    /// 外から作った毛を、この場の落下の仕組みに引き渡す。
    /// オープニングで見せていた一本を、そのまま地表へ降る一本目にするために使う。
    /// </summary>
    public void AdoptHair(PlanetHair h)
    {
        if (h == null || h.GetComponent<FieldHair>() != null) return;
        if (_heads == null || _heads.Length == 0)
            _heads = FindObjectsByType<OrbitingHead>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Attach(h);
    }

    void Attach(PlanetHair h)
    {
        Vector3 pos = h.transform.position;

        // この毛が離れた頭。生えていた場所のすぐ近くで抜けるので、一番近い頭でよい。
        OrbitingHead src = null; float best = float.MaxValue;
        foreach (var hd in _heads)
        {
            if (hd == null) continue;
            float d = (hd.transform.position - pos).sqrMagnitude;
            if (d < best) { best = d; src = hd; }
        }

        // 頭が動いていた速度をそのまま持って離れる。慣性。
        Vector3 v0 = Vector3.zero;
        if (src != null && _headVel.TryGetValue(src, out Vector3 hv)) v0 = hv * inheritHeadVelocity;
        // わずかなばらけ。同じ頭から同時に抜けた毛が一本の線に重ならないように。
        v0 += Random.onUnitSphere * (v0.magnitude * releaseScatter);

        var fh = h.gameObject.AddComponent<FieldHair>();
        fh.Init(_globe, _center, _landRadius, v0, gravity, drag, h.ownerName);
    }
}

/// <summary>
/// 右クリックを、動かさずに離したときだけ「もやの点を置く」として扱う。
/// 右ドラッグは何もしない（既存シーンの視点操作は左ドラッグなので、取り合いにならない）。
/// </summary>
public class ProtoRightClickPlacer : MonoBehaviour
{
    public StaticLineField field;
    public float clickThreshold = 6f;
    float _drag;

    void Update()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null || field == null) return;

        if (mouse.rightButton.wasPressedThisFrame) _drag = 0f;
        if (mouse.rightButton.isPressed) _drag += mouse.delta.ReadValue().magnitude;
        if (mouse.rightButton.wasReleasedThisFrame && _drag < clickThreshold)
            field.PlaceNodeAtScreen(mouse.position.ReadValue());
    }
}
