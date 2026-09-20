using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// 静電気のもや。
///
/// クリックで星を包む見えない球殻の上に点を置き、置いた順に点を結んで帯を張る。
/// 一本ずつ引くのではなく、点を打って結ぶ操作にしてある。線が引かれた瞬間ではなく、
/// 「どこに張るか」を決める時間のほうに判断が寄る。落ちてくる毛に反射で対応するのではなく、
/// 落ちてくるであろう場所に先回りして構造を組む。架構を組む操作に近い。
///
/// 見た目は細い線ではなく、白く柔らかい帯にしてある。線だと、そこに溜まった毛の塊が
/// 線自体に埋もれて見えなくなる。うすい白のもやなら、集まった黒い毛がその中で影のように
/// 浮かび上がる。静電気は本来かたちを持たないので、輪郭のない表現のほうが素直でもある。
///
/// 張られたもやは毛を吸い寄せる。捕まった毛はもやの中に留まり、重力を逃れて漂い、
/// 互いに寄り集まって房になる。落下は止まらない。ただ、どこへ落ちるかと、
/// どれだけの時間をかけるかが変わる。
/// </summary>
public class StaticLineField : MonoBehaviour
{
    // 再生中にスクリプトを編集するとドメインリロードが起き、静的フィールドだけが消えて
    // シーンのオブジェクトは残る。Awake はもう走らないので、参照が null のまま戻らなくなる。
    // 見つからなければ探し直す形にしておけば、編集しながらの検証で引っかからない。
    static StaticLineField _instance;
    public static StaticLineField Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<StaticLineField>();
            return _instance;
        }
        private set { _instance = value; }
    }

    [Header("場")]
    public Vector3 center = Vector3.zero;
    [Tooltip("点を置く球殻の半径。地球半径より大きく、頭の周回半径より小さいあたり。")]
    public float shellRadius = 3.2f;

    [Header("吸着")]
    // 力の大きさは重力（既定 0.25）と釣り合う程度にしてある。
    // これより強くすると毛がもやに吸い付いて跳ね、弱くすると素通りする。
    [Tooltip("この距離まで近づいた毛がもやに引かれる。")]
    public float attractRadius = 0.5f;
    [Tooltip("もやへ引き寄せる力の強さ。")]
    public float attractStrength = 0.9f;
    [Tooltip("もやに沿って流す力。大きいほど毛が帯を長く旅する。0に近いとその場に溜まる。")]
    public float slideStrength = 0.16f;
    [Tooltip("もやの中で重力をどれだけ打ち消すか。1で完全に無重力、0で効果なし。")]
    [Range(0f, 1f)] public float holdAgainstGravity = 0.98f;
    [Tooltip("捕まったと見なす距離。この内側で重力の打ち消しと凝集が効く。")]
    public float holdRadius = 0.24f;

    [Header("毛どうしの凝集")]
    [Tooltip("もやの中で捕まった毛が、互いに寄り集まる力。房をつくる。")]
    public float cohesionStrength = 0.30f;
    [Tooltip("凝集が効く距離。")]
    public float cohesionRadius = 0.28f;
    [Tooltip("これ以上は近づかない距離。潰れて一点にならないようにする。")]
    public float cohesionMinDistance = 0.055f;

    [Header("見た目")]
    public Color hazeColor = new Color(1f, 1f, 1f);
    [Tooltip("もやの太さ。吸着範囲と揃えておくと、見えている範囲に毛が集まる。")]
    public float hazeWidth = 0.28f;
    // 加算合成なので、背景が明るいと消え、暗いと強く出る。宇宙を背景にした状態で、
    // 地表が透けるくらいの薄さに合わせてある。濃くすると、もやの中に溜まった黒い毛が
    // 白に埋もれて見えなくなる。
    [Range(0f, 1f)] public float hazeOpacity = 0.07f;
    [Tooltip("もやの明滅の速さ。静電気らしいゆらぎ。")]
    public float shimmerSpeed = 0.7f;
    [Tooltip("一区間を球殻に沿わせるための分割数。少ないと折れ目に角が出る。")]
    public int segmentSubdivision = 34;

    [Header("入力")]
    public Camera targetCamera;

    // 引いている途中のポリライン
    readonly List<Vector3> _current = new List<Vector3>();
    // 確定済みの区間（始点・終点のペア）
    readonly List<(Vector3 a, Vector3 b)> _segments = new List<(Vector3, Vector3)>();

    readonly List<LineRenderer> _renderers = new List<LineRenderer>();
    // いま引いているポリラインを描く一本の帯。区間ごとに分けると継ぎ目が箱に見えるので、
    // 点が増えるたびに全体を引き直す。
    LineRenderer _currentRibbon;
    LineRenderer _preview;
    Material _hazeMat, _previewMat, _nodeMat;
    Texture2D _softTex;
    Transform _nodeRoot;
    readonly List<Renderer> _nodeRenderers = new List<Renderer>();

    void Awake()
    {
        Instance = this;
        EnsureVisuals();
    }

    /// <summary>ドメインリロード後でもマテリアルが失われていないように、必要になった時点で作る。</summary>
    void EnsureVisuals()
    {
        if (_hazeMat != null) return;
        _softTex   = BuildSoftTexture();
        _hazeMat    = BuildHazeMaterial(hazeOpacity);
        _previewMat = BuildHazeMaterial(hazeOpacity * 0.5f);
        _nodeMat    = BuildHazeMaterial(hazeOpacity * 2.2f);
        if (_nodeRoot == null)
        {
            _nodeRoot = new GameObject("Nodes").transform;
            _nodeRoot.SetParent(transform, false);
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>中心が濃く縁が消える、輪郭のない帯のためのテクスチャ。</summary>
    static Texture2D BuildSoftTexture()
    {
        const int N = 64;
        var tex = new Texture2D(4, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < N; y++)
        {
            float t = (y + 0.5f) / N * 2f - 1f;               // -1..1
            float a = Mathf.Exp(-t * t * 7.0f);               // 中心が濃く、外へ急に薄れる
            a *= Mathf.SmoothStep(0f, 1f, (1f - Mathf.Abs(t)) * 1.6f); // 端は完全に0へ
            var c = new Color(1f, 1f, 1f, a);
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return tex;
    }

    /// <summary>加算合成の半透明マテリアル。暗い宇宙でも明るい地表でも、白く薄く乗る。</summary>
    Material BuildHazeMaterial(float opacity)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetTexture("_BaseMap", _softTex);
        m.SetColor("_BaseColor", new Color(hazeColor.r, hazeColor.g, hazeColor.b, opacity));
        m.SetFloat("_Surface", 1f);                                  // Transparent
        m.SetFloat("_Blend", 2f);                                    // Additive
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_AlphaClip", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    void Update()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null || targetCamera == null) return;

        // 点を置くのは右クリック。素の左クリックは地表の毛を選ぶために空けてある。
        // 毛が積もってくると、見たいものと張りたい場所が画面上で重なるので、はっきり分けておく。
        //
        // Cmd / Ctrl を押しながらなら押した瞬間に置く。ただし macOS では Cmd が
        // エディタ側に吸われて Unity まで届かないことがあるので、修飾キー無しの
        // 「右クリックを動かさずに離す」でも置けるようにしてある（カメラ側から呼ばれる）。
        if (mouse.rightButton.wasPressedThisFrame && ModifierHeld())
            PlaceNodeAtScreen(mouse.position.ReadValue());

        // Enter / Esc: いま引いているポリラインを終える
        if (kb != null && (kb.enterKey.wasPressedThisFrame
                        || kb.numpadEnterKey.wasPressedThisFrame
                        || kb.escapeKey.wasPressedThisFrame)) EndPolyline();

        // C: 全消去
        if (kb != null && kb.cKey.wasPressedThisFrame) ClearAll();

        UpdatePreview(mouse.position.ReadValue());
        Shimmer();
    }

    /// <summary>画面上の点から球殻上に点を置く。カメラ側からも呼ぶ。</summary>
    public void PlaceNodeAtScreen(Vector2 screenPos)
    {
        if (TryPickShell(screenPos, out Vector3 p)) AddNode(p);
    }

    /// <summary>いま引いている途中か。プレビューの表示や、案内文の切り替えに使う。</summary>
    public bool IsDrawing => _current.Count > 0;
    public int  NodeCount => _current.Count;
    public int  SegmentCount => _segments.Count;

    /// <summary>Cmd か Ctrl。どちらの環境でも同じように使えるよう両方受ける。</summary>
    public static bool ModifierHeld()
    {
        var kb = Keyboard.current;
        if (kb == null) return false;
        return kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed
            || kb.leftCtrlKey.isPressed    || kb.rightCtrlKey.isPressed;
    }

    /// <summary>もやをゆっくり明滅させる。静電気が場に満ちている気配。</summary>
    void Shimmer()
    {
        float k = 1f + Mathf.Sin(Time.time * shimmerSpeed) * 0.22f;
        var c = new Color(hazeColor.r, hazeColor.g, hazeColor.b, hazeOpacity * k);
        if (_hazeMat != null) _hazeMat.SetColor("_BaseColor", c);
        if (_nodeMat != null) _nodeMat.SetColor("_BaseColor",
            new Color(hazeColor.r, hazeColor.g, hazeColor.b, hazeOpacity * 2.2f * k));
    }

    /// <summary>画面の点から、星を包む球殻の手前側の交点を求める。</summary>
    bool TryPickShell(Vector2 screenPos, out Vector3 hit)
    {
        hit = default;
        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        Vector3 oc = ray.origin - center;
        float b = Vector3.Dot(oc, ray.direction);
        float c = Vector3.Dot(oc, oc) - shellRadius * shellRadius;
        float disc = b * b - c;
        if (disc < 0f)
        {
            // 球殻の外側をクリックしたとき。何も起きないと押し損ねたように感じるので、
            // 視線に一番近い殻の上の点へ寄せる。縁に沿って置ける。
            Vector3 nearest = ray.origin + ray.direction * Mathf.Max(-b, 0f);
            Vector3 dir = nearest - center;
            if (dir.sqrMagnitude < 1e-9f) return false;
            hit = center + dir.normalized * shellRadius;
            return true;
        }
        float sq = Mathf.Sqrt(disc);
        float t = -b - sq;                 // 手前側
        if (t < 0f) t = -b + sq;           // カメラが殻の内側にいる場合
        if (t < 0f) return false;
        hit = ray.origin + ray.direction * t;
        return true;
    }

    void AddNode(Vector3 p)
    {
        EnsureVisuals();
        if (_current.Count > 0)
        {
            Vector3 prev = _current[_current.Count - 1];
            if (Vector3.Distance(prev, p) < 1e-3f) return;
            _segments.Add((prev, p));
        }
        _current.Add(p);
        RebuildCurrentRibbon();
    }

    public void EndPolyline()
    {
        if (_currentRibbon != null) _renderers.Add(_currentRibbon);
        _currentRibbon = null;
        _current.Clear();
        if (_preview != null) _preview.positionCount = 0;
    }

    public void ClearAll()
    {
        _current.Clear();
        _segments.Clear();
        foreach (var lr in _renderers) if (lr != null) Destroy(lr.gameObject);
        _renderers.Clear();
        if (_currentRibbon != null) { Destroy(_currentRibbon.gameObject); _currentRibbon = null; }
        _nodeRenderers.Clear();
        if (_nodeRoot != null)
            for (int i = _nodeRoot.childCount - 1; i >= 0; i--) Destroy(_nodeRoot.GetChild(i).gameObject);
        if (_preview != null) _preview.positionCount = 0;
    }

    // ------------------------------------------------------------------
    // 毛へ返す力
    // ------------------------------------------------------------------

    /// <summary>
    /// 位置 pos にある毛が、もやから受ける加速度。
    /// caught は、もやの中に捕まっていると見なせる状態か。
    /// </summary>
    public Vector3 ForceOn(Vector3 pos, Vector3 vel, out bool caught)
    {
        caught = false;
        if (_segments.Count == 0) return Vector3.zero;

        // 一番近い区間を探す
        float bestD = float.MaxValue;
        Vector3 bestPoint = Vector3.zero, bestDir = Vector3.zero;
        for (int i = 0; i < _segments.Count; i++)
        {
            Vector3 a = _segments[i].a, b = _segments[i].b;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-9f ? 0f : Mathf.Clamp01(Vector3.Dot(pos - a, ab) / len2);
            Vector3 q = a + ab * t;
            float d = Vector3.Distance(pos, q);
            if (d < bestD) { bestD = d; bestPoint = q; bestDir = len2 < 1e-9f ? Vector3.zero : ab.normalized; }
        }

        if (bestD > attractRadius) return Vector3.zero;

        Vector3 toLine = bestPoint - pos;
        float falloff = 1f - bestD / attractRadius;      // 近いほど強い
        Vector3 force = toLine.normalized * (attractStrength * falloff);

        if (bestD <= holdRadius)
        {
            caught = true;

            // もやに沿ってゆっくり流す。強くすると溜まらずに流れ去ってしまう。
            float sign = Vector3.Dot(vel, bestDir) >= 0f ? 1f : -1f;
            force += bestDir * (sign * slideStrength);

            // 重力の打ち消し。もやが毛を支えている間、落下がほとんど止まる。
            Vector3 up = (pos - center).normalized;
            float hold = holdAgainstGravity * Mathf.SmoothStep(0f, 1f, 1f - bestD / holdRadius);
            force += up * (hold * ProtoGravityHint);
        }

        return force;
    }

    /// <summary>重力打ち消しに使う値。生成側が起動時に設定する。</summary>
    [HideInInspector] public float ProtoGravityHint = 0.25f;

    // ------------------------------------------------------------------
    // 描画
    // ------------------------------------------------------------------

    LineRenderer MakeRibbon(string name, Material mat, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = mat;
        lr.widthMultiplier = width;
        lr.useWorldSpace = true;
        lr.numCapVertices = 8;
        lr.alignment = LineAlignment.View;          // 常にカメラを向く帯
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        return lr;
    }

    /// <summary>球殻に沿ってポリライン全体をなめらかに引き直す。</summary>
    void RebuildCurrentRibbon()
    {
        if (_currentRibbon == null) _currentRibbon = MakeRibbon("Haze", _hazeMat, hazeWidth);
        if (_current.Count < 2) { _currentRibbon.positionCount = 0; return; }

        int per = Mathf.Max(2, segmentSubdivision);
        var pts = new List<Vector3>();
        for (int s = 0; s + 1 < _current.Count; s++)
        {
            Vector3 ua = (_current[s] - center).normalized;
            Vector3 ub = (_current[s + 1] - center).normalized;
            int start = s == 0 ? 0 : 1;                       // 継ぎ目の点を重複させない
            for (int i = start; i < per; i++)
                pts.Add(center + Vector3.Slerp(ua, ub, (float)i / (per - 1)) * shellRadius);
        }
        _currentRibbon.positionCount = pts.Count;
        _currentRibbon.SetPositions(pts.ToArray());
        // 帯の両端だけを細めて、切り口を出さない
        _currentRibbon.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.25f), new Keyframe(0.06f, 1f),
            new Keyframe(0.94f, 1f), new Keyframe(1f, 0.25f));
    }

    /// <summary>次の点までの帯を薄く見せる。どこへ張られるかを見ながら置けるようにする。</summary>
    void UpdatePreview(Vector2 screenPos)
    {
        // 引いている途中なら、次にどこへ張られるかを常に見せる。
        if (_current.Count == 0)
        {
            if (_preview != null) _preview.positionCount = 0;
            return;
        }
        if (_preview == null) _preview = MakeRibbon("Preview", _previewMat, hazeWidth * 0.7f);
        if (!TryPickShell(screenPos, out Vector3 p)) { _preview.positionCount = 0; return; }

        Vector3 a = _current[_current.Count - 1];
        int n = Mathf.Max(2, segmentSubdivision);
        _preview.positionCount = n;
        Vector3 ua = (a - center).normalized, ub = (p - center).normalized;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            _preview.SetPosition(i, center + Vector3.Slerp(ua, ub, t) * shellRadius);
        }
    }
}

/// <summary>板を常にカメラへ向ける。もやの粒が板に見えないように。</summary>
public class ProtoBillboard : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        transform.rotation = cam.transform.rotation;
    }
}
