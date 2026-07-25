using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 直立している人の横から、突然 虎が現れる。人は驚いて飛び跳ねる。
/// その瞬間に頭をクリックすると、頭皮に生えた一本の毛の画面へ「ぱっと」切り替わる。
/// 毛もまた、同じ拍子で驚いて飛び跳ねている。
///
/// 二つの画面は同じ画角で撮られ、驚きの拍子だけが共有される。
/// 毛の側に虎は現れない —— そのスケールでは原因は見えず、反応だけが伝わる。
/// 何に驚いているのか分からないまま驚いている、という状態を作るための省略。
///
/// 既存スクリプトには手を加えない独立ファイル。空の GameObject にアタッチして使う。
/// 虎・毛・頭皮はすべてコードで生成するため、追加のモデルは不要。
/// </summary>
public class TigerScareHairJump : MonoBehaviour
{
    [Header("対象（未設定なら自動検出）")]
    public Transform humanRoot;      // 人のルート
    public Transform headTarget;     // クリック判定に使う頭
    [Tooltip("クリック判定の太さ（ワールド単位）")]
    public float clickRadius = 0.25f;

    [Header("虎")]
    [Tooltip("人から見てどちら側から現れるか（-1で左、+1で右）")]
    public float approachSide = -1f;
    [Tooltip("虎が待機する、人からの距離")]
    public float tigerStandoff = 1.5f;
    [Tooltip("人より手前(カメラ側)にどれだけずらすか。人の陰に隠れないように。")]
    public float tigerDepthOffset = -0.45f;
    [Tooltip("虎の大きさ")]
    public float tigerScale = 1.3f;
    [Tooltip("画面外の位置（人からの距離）")]
    public float tigerOffstage = 7f;
    [Tooltip("飛び出してくる速さ（秒）")]
    public float tigerRushSeconds = 0.42f;
    [Tooltip("居座る時間（秒）")]
    public float tigerStaySeconds = 1.6f;
    [Tooltip("次に現れるまでの間隔（秒）")]
    public float scareIntervalMin = 3.5f;
    public float scareIntervalMax = 6.5f;

    [Header("跳ねる")]
    public float jumpHeight = 0.55f;
    public float jumpSeconds = 0.62f;
    [Tooltip("毛の跳ねる高さの倍率（毛は人より小柄なので少し控えめに）")]
    public float hairJumpScale = 0.72f;

    [Header("毛の画面")]
    public Vector3 hairSceneOrigin = new Vector3(0f, 1000f, 0f);
    public Color hairColor = new Color(0.10f, 0.09f, 0.08f);
    public Color scalpColor = new Color(0.93f, 0.80f, 0.70f);
    public Color backgroundColor = new Color(0.93f, 0.92f, 0.90f);
    [Tooltip("人が椅子/直立しているビューと同じ画角で毛を映す")]
    public bool matchMainCameraFraming = true;

    [Header("声")]
    public AudioClip voiceClip;
    public float voiceIntervalMin = 5f;
    public float voiceIntervalMax = 11f;
    [Range(0f, 1f)] public float voiceVolume = 1f;
    [Tooltip("驚いた瞬間にも声を出す")]
    public bool speakOnScare = true;

    [Header("戻る")]
    public bool allowReturn = true;

    // ---- 内部 ----
    enum TigerState { Waiting, Rushing, Staying, Leaving }

    Camera _mainCam, _hairCam;
    Transform _hairRoot, _strand, _tiger;
    AudioSource _voice;

    bool _inHairView;
    float _humanBaseY;
    float _jumpT = -1f;          // 負なら跳んでいない
    float _nextVoiceAt;
    float _speakT = -1f;

    TigerState _tigerState = TigerState.Waiting;
    float _stateT;
    float _waitFor;

    readonly RaycastHit[] _hits = new RaycastHit[16];

    void Start()
    {
        _mainCam = Camera.main;

        if (humanRoot == null)
        {
            var h = GameObject.Find("Human");
            if (h != null) humanRoot = h.transform;
        }
        if (headTarget == null && humanRoot != null) headTarget = humanRoot.Find("Head");

        if (humanRoot != null) _humanBaseY = humanRoot.position.y;

        BuildTiger();
        BuildHairScene();
        SetHairView(false);

        _waitFor = Random.Range(scareIntervalMin, scareIntervalMax) * 0.4f;
    }

    void Update()
    {
        UpdateTiger();
        UpdateJump();

        if (_inHairView)
        {
            AlignHairCameraToMain();
            UpdateVoice();
            if (allowReturn && (ClickedThisFrame() || EscapedThisFrame())) SetHairView(false);
            return;
        }

        if (!ClickedThisFrame()) return;
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null || headTarget == null) return;

        Ray ray = _mainCam.ScreenPointToRay(PointerPosition());
        int n = Physics.SphereCastNonAlloc(ray, clickRadius, _hits, Mathf.Infinity);
        for (int i = 0; i < n; i++)
        {
            var col = _hits[i].collider;
            if (col == null) continue;
            if (col.transform == headTarget || col.transform.IsChildOf(headTarget))
            {
                SetHairView(true);
                break;
            }
        }
    }

    // ------------------------------------------------------------------
    // 虎の出入り
    // ------------------------------------------------------------------

    void UpdateTiger()
    {
        if (_tiger == null || humanRoot == null) return;

        float dt = Time.deltaTime;
        _stateT += dt;

        // 人は驚いて跳ね上がるので、その現在位置を基準にすると虎まで一緒に浮いてしまう。
        // 虎は地面に留まるものなので、人の接地高さを基準にする。
        Vector3 basePos = humanRoot.position;
        basePos.y = _humanBaseY;

        float far = tigerOffstage * approachSide;
        float near = tigerStandoff * approachSide;

        switch (_tigerState)
        {
            case TigerState.Waiting:
                _tiger.position = basePos + new Vector3(far, 0f, tigerDepthOffset);
                if (_stateT >= _waitFor) Go(TigerState.Rushing);
                break;

            case TigerState.Rushing:
                {
                    float u = Mathf.Clamp01(_stateT / Mathf.Max(0.01f, tigerRushSeconds));
                    // 一気に詰めて、寸前で止まる
                    float e = 1f - Mathf.Pow(1f - u, 3f);
                    _tiger.position = basePos + new Vector3(Mathf.Lerp(far, near, e), 0f, tigerDepthOffset);
                    if (u >= 1f)
                    {
                        Go(TigerState.Staying);
                        Startle();
                    }
                    break;
                }

            case TigerState.Staying:
                // 上下には動かさない。虎は跳ねず、地に足をつけたまま睨んでいる。
                _tiger.position = basePos + new Vector3(near, 0f, tigerDepthOffset);
                if (_stateT >= tigerStaySeconds) Go(TigerState.Leaving);
                break;

            case TigerState.Leaving:
                {
                    float u = Mathf.Clamp01(_stateT / 0.5f);
                    _tiger.position = basePos + new Vector3(Mathf.Lerp(near, far, u * u), 0f, tigerDepthOffset);
                    if (u >= 1f)
                    {
                        _waitFor = Random.Range(scareIntervalMin, scareIntervalMax);
                        Go(TigerState.Waiting);
                    }
                    break;
                }
        }
    }

    void Go(TigerState s) { _tigerState = s; _stateT = 0f; }

    /// <summary>驚きの拍子。人も毛も同じ瞬間に跳ねる。</summary>
    void Startle()
    {
        _jumpT = 0f;
        if (speakOnScare && _inHairView) Speak();
    }

    // ------------------------------------------------------------------
    // 跳ねる
    // ------------------------------------------------------------------

    void UpdateJump()
    {
        if (_jumpT < 0f) return;
        _jumpT += Time.deltaTime;

        float u = _jumpT / Mathf.Max(0.01f, jumpSeconds);
        if (u >= 1f)
        {
            _jumpT = -1f;
            if (humanRoot != null) SetY(humanRoot, _humanBaseY);
            if (_strand != null) _strand.localPosition = new Vector3(0f, 0f, 0f);
            return;
        }

        // ぴょんと上がって落ちる。立ち上がりを速くして「びくっ」とさせる。
        float arc = Mathf.Sin(Mathf.Pow(u, 0.75f) * Mathf.PI);

        if (humanRoot != null) SetY(humanRoot, _humanBaseY + arc * jumpHeight);
        if (_strand != null) _strand.localPosition = new Vector3(0f, arc * jumpHeight * hairJumpScale, 0f);
    }

    static void SetY(Transform t, float y)
    {
        Vector3 p = t.position; p.y = y; t.position = p;
    }

    // ------------------------------------------------------------------
    // 画面の切り替え
    // ------------------------------------------------------------------

    void SetHairView(bool on)
    {
        _inHairView = on;

        if (_hairRoot != null) _hairRoot.gameObject.SetActive(on);
        if (on) AlignHairCameraToMain();
        if (_hairCam != null) _hairCam.enabled = on;

        // メインカメラは GameObject ごと止めない（AudioListener が一緒に死んで声が鳴らなくなる）
        if (_mainCam != null) _mainCam.enabled = !on;

        if (on) _nextVoiceAt = Time.time + 1.0f;
        else if (_voice != null && _voice.isPlaying) { _voice.Stop(); _speakT = -1f; }
    }

    /// <summary>
    /// 人のビューのカメラ位置・向き・画角を、人を基準にした相対値として毛の場へ移す。
    /// 二つの画面が同じ構図になり、切り替えが「同じ場所で別のものが跳ねている」入れ替えとして読める。
    /// </summary>
    void AlignHairCameraToMain()
    {
        if (!matchMainCameraFraming || _hairCam == null || _hairRoot == null || humanRoot == null) return;
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        Vector3 rel = humanRoot.InverseTransformPoint(_mainCam.transform.position);
        // 人は跳ねて上下するので、その分だけカメラの相対高さが揺れてしまう。基準の高さに戻して測る。
        rel.y += (humanRoot.position.y - _humanBaseY);

        Quaternion relRot = Quaternion.Inverse(humanRoot.rotation) * _mainCam.transform.rotation;

        _hairCam.transform.position = _hairRoot.TransformPoint(rel);
        _hairCam.transform.rotation = _hairRoot.rotation * relRot;
        _hairCam.fieldOfView = _mainCam.fieldOfView;
    }

    // ------------------------------------------------------------------
    // 声
    // ------------------------------------------------------------------

    void UpdateVoice()
    {
        if (Time.time >= _nextVoiceAt)
        {
            Speak();
            _nextVoiceAt = Time.time + Random.Range(voiceIntervalMin, voiceIntervalMax);
        }

        if (_strand != null)
        {
            float wobble = 0f;
            if (_speakT >= 0f)
            {
                _speakT += Time.deltaTime;
                float dur = (_voice != null && _voice.clip != null) ? _voice.clip.length : 1.2f;
                if (_speakT > dur) _speakT = -1f;
                else wobble = Mathf.Sin(_speakT * 16f) * 0.05f * Mathf.Clamp01((dur - _speakT) / dur);
            }
            float breathe = Mathf.Sin(Time.time * 1.3f) * 0.010f;
            _strand.localRotation = Quaternion.Euler(0f, 0f, (wobble + breathe) * 60f);
        }
    }

    void Speak()
    {
        if (_voice == null || _voice.clip == null) return;
        _voice.volume = voiceVolume;
        _voice.Play();
        _speakT = 0f;
        _nextVoiceAt = Time.time + Random.Range(voiceIntervalMin, voiceIntervalMax);
    }

    // ------------------------------------------------------------------
    // 虎をコードで組み立てる
    // ------------------------------------------------------------------

    void BuildTiger()
    {
        var root = new GameObject("Tiger").transform;
        root.SetParent(transform, false);
        // 人の方（+X 側）を向かせる
        root.localRotation = Quaternion.Euler(0f, approachSide < 0f ? 90f : -90f, 0f);
        root.localScale = Vector3.one * tigerScale;
        _tiger = root;

        var fur = MakeMaterial(new Color(0.85f, 0.45f, 0.10f));
        var dark = MakeMaterial(new Color(0.10f, 0.08f, 0.07f));
        var pale = MakeMaterial(new Color(0.96f, 0.93f, 0.88f));

        // 胴（+Z が前）
        MakeBox(root, "Body", new Vector3(0f, 0.52f, 0f), new Vector3(0.36f, 0.34f, 0.92f), fur);
        // 頭
        MakeBox(root, "Head", new Vector3(0f, 0.66f, 0.60f), new Vector3(0.34f, 0.30f, 0.30f), fur);
        MakeBox(root, "Muzzle", new Vector3(0f, 0.60f, 0.76f), new Vector3(0.20f, 0.16f, 0.10f), pale);
        MakeBox(root, "EarL", new Vector3(-0.11f, 0.83f, 0.58f), new Vector3(0.09f, 0.09f, 0.04f), fur);
        MakeBox(root, "EarR", new Vector3(0.11f, 0.83f, 0.58f), new Vector3(0.09f, 0.09f, 0.04f), fur);
        MakeBox(root, "EyeL", new Vector3(-0.09f, 0.70f, 0.755f), new Vector3(0.06f, 0.05f, 0.02f), dark);
        MakeBox(root, "EyeR", new Vector3(0.09f, 0.70f, 0.755f), new Vector3(0.06f, 0.05f, 0.02f), dark);

        // 脚
        MakeBox(root, "LegFL", new Vector3(-0.13f, 0.18f, 0.32f), new Vector3(0.11f, 0.36f, 0.12f), fur);
        MakeBox(root, "LegFR", new Vector3(0.13f, 0.18f, 0.32f), new Vector3(0.11f, 0.36f, 0.12f), fur);
        MakeBox(root, "LegBL", new Vector3(-0.13f, 0.18f, -0.32f), new Vector3(0.11f, 0.36f, 0.12f), fur);
        MakeBox(root, "LegBR", new Vector3(0.13f, 0.18f, -0.32f), new Vector3(0.11f, 0.36f, 0.12f), fur);

        // 尾
        MakeBox(root, "Tail", new Vector3(0f, 0.66f, -0.56f), new Vector3(0.08f, 0.08f, 0.34f), fur);

        // 縞
        for (int i = 0; i < 5; i++)
        {
            float z = -0.34f + i * 0.17f;
            MakeBox(root, "Stripe" + i, new Vector3(0f, 0.52f, z), new Vector3(0.372f, 0.345f, 0.045f), dark);
        }
    }

    // ------------------------------------------------------------------
    // 毛の画面をコードで組み立てる
    // ------------------------------------------------------------------

    void BuildHairScene()
    {
        var root = new GameObject("HairJumpView").transform;
        root.SetParent(transform, false);
        root.position = hairSceneOrigin;
        _hairRoot = root;

        var camGo = new GameObject("HairCamera");
        camGo.transform.SetParent(root, false);
        camGo.transform.localPosition = new Vector3(2.4f, 1.25f, -2.6f);
        camGo.transform.localRotation = Quaternion.LookRotation(
            (new Vector3(0f, 0.85f, 0f) - camGo.transform.localPosition).normalized, Vector3.up);
        _hairCam = camGo.AddComponent<Camera>();
        _hairCam.clearFlags = CameraClearFlags.SolidColor;
        _hairCam.backgroundColor = backgroundColor;
        _hairCam.nearClipPlane = 0.05f;
        _hairCam.farClipPlane = 200f;

        var lightGo = new GameObject("HairLight");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localRotation = Quaternion.Euler(38f, -28f, 0f);
        var lt = lightGo.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.1f;

        // 頭皮 —— 巨大な球の頂部が、わずかに丸い大地として見える
        var scalp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        scalp.name = "Scalp";
        scalp.transform.SetParent(root, false);
        scalp.transform.localScale = Vector3.one * 40f;
        scalp.transform.localPosition = new Vector3(0f, -20f, 0f);
        StripCollider(scalp);
        scalp.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(scalpColor);

        _strand = BuildStandingStrand(root, MakeMaterial(hairColor));

        _voice = gameObject.AddComponent<AudioSource>();
        _voice.playOnAwake = false;
        _voice.spatialBlend = 0f;
        _voice.clip = voiceClip;
    }

    /// <summary>
    /// 直立している一本の毛。まっすぐではなく、ゆるく身をよじった折れ線にして、
    /// 立っている「姿勢」として読めるようにする。
    /// </summary>
    Transform BuildStandingStrand(Transform root, Material mat)
    {
        var strand = new GameObject("Strand").transform;
        strand.SetParent(root, false);
        strand.localPosition = Vector3.zero;

        Vector3 p0 = new Vector3(0f, 0.00f, 0f);
        Vector3 p1 = new Vector3(0f, 0.42f, -0.05f);
        Vector3 p2 = new Vector3(0f, 0.86f, 0.03f);
        Vector3 p3 = new Vector3(0f, 1.22f, -0.04f);
        Vector3 p4 = new Vector3(0f, 1.44f, 0.02f);

        const float thick = 0.055f;
        MakeSegment(strand, "Root", p0, p1, thick * 1.05f, mat);
        MakeSegment(strand, "Mid", p1, p2, thick, mat);
        MakeSegment(strand, "Upper", p2, p3, thick * 0.95f, mat);
        MakeSegment(strand, "Neck", p3, p4, thick * 0.9f, mat);

        MakeJoint(strand, "J1", p1, thick, mat);
        MakeJoint(strand, "J2", p2, thick * 0.98f, mat);
        MakeJoint(strand, "J3", p3, thick * 0.95f, mat);
        MakeJoint(strand, "Tip", p4, thick * 1.1f, mat);

        return strand;
    }

    // ------------------------------------------------------------------
    // 生成ヘルパー
    // ------------------------------------------------------------------

    void MakeSegment(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 dir = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        go.transform.localScale = new Vector3(thickness, dir.magnitude * 0.5f, thickness);
        StripCollider(go);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    void MakeJoint(Transform parent, string name, Vector3 at, float thickness, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localScale = Vector3.one * thickness;
        StripCollider(go);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    void MakeBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        StripCollider(go);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    Material MakeMaterial(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        return m;
    }

    static void StripCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    // ------------------------------------------------------------------
    // 入力
    // ------------------------------------------------------------------

    bool ClickedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    bool EscapedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return (Vector2)Input.mousePosition;
#endif
    }
}
