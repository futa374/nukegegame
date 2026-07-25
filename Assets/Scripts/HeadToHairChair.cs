using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 頭をクリックすると、頭皮に生えた一本の毛の画面へ「ぱっと」切り替わる。
///
/// 切り替わった先では、頭皮から生えた毛が椅子に腰かけたような形をしている。
/// 座っている人の頭の中に、また座っている一本の毛がいる——という入れ子の構図で、
/// 冒頭の Powers of Ten 的なスケールの降下を、一段だけ内側へ折り返したもの。
/// その毛はときどき声を発する（Assets/Audio/monden_voice_huhuhu）。
///
/// 既存スクリプトには一切手を加えない独立ファイル。空の GameObject にアタッチして使う。
/// 毛と椅子はすべてコードで生成するため、追加のモデルは不要。
/// </summary>
public class HeadToHairChair : MonoBehaviour
{
    [Header("クリック対象")]
    [Tooltip("未設定なら Human/Head を自動で探す")]
    public Transform headTarget;
    [Tooltip("クリック判定の太さ（ワールド単位）")]
    public float clickRadius = 0.25f;

    [Header("毛の画面")]
    [Tooltip("毛の画面を置く場所。元のシーンと干渉しないよう遠くに置く")]
    public Vector3 hairSceneOrigin = new Vector3(0f, 1000f, 0f);
    public Color hairColor = new Color(0.10f, 0.09f, 0.08f);
    public Color scalpColor = new Color(0.93f, 0.80f, 0.70f);
    public Color chairColor = new Color(0.42f, 0.44f, 0.48f);
    public Color backgroundColor = new Color(0.93f, 0.92f, 0.90f);

    [Header("声")]
    [Tooltip("未設定なら Resources/Audio か、Inspector で直接指定する")]
    public AudioClip voiceClip;
    [Tooltip("声を発する間隔（秒）の下限・上限")]
    public float voiceIntervalMin = 5f;
    public float voiceIntervalMax = 12f;
    [Range(0f, 1f)] public float voiceVolume = 1f;

    [Header("画角")]
    [Tooltip("人が椅子に座っているビューと同じ画角で毛を映す。切り替えが入れ替えとして読めるようになる。")]
    public bool matchMainCameraFraming = true;

    [Header("戻る")]
    [Tooltip("クリックまたは Esc で元の画面へ戻れるようにする")]
    public bool allowReturn = true;

    // ---- 内部 ----
    Camera _mainCam;
    Camera _hairCam;
    Transform _subjectRoot;     // 椅子に座っている人のルート。画角を合わせる基準になる。
    Transform _hairRoot;
    Transform _strand;          // 毛の本体（声に合わせて揺らす）
    AudioSource _voice;
    bool _inHairView;
    float _nextVoiceAt;
    float _speakT = -1f;        // 発声中の演出用タイマー（負なら発声していない）
    readonly RaycastHit[] _hits = new RaycastHit[16];

    void Start()
    {
        _mainCam = Camera.main;
        if (headTarget == null)
        {
            var human = GameObject.Find("Human");
            if (human != null) headTarget = human.transform.Find("Head");
            if (headTarget == null)
            {
                var h = GameObject.Find("Head");
                if (h != null) headTarget = h.transform;
            }
        }

        // 画角合わせの基準は「人のルート」。頭の親、なければ Human を使う。
        if (headTarget != null) _subjectRoot = headTarget.parent;
        if (_subjectRoot == null)
        {
            var human = GameObject.Find("Human");
            if (human != null) _subjectRoot = human.transform;
        }

        BuildHairScene();
        SetHairView(false);
    }

    void Update()
    {
        if (_inHairView)
        {
            UpdateHairView();

            if (allowReturn && (ClickedThisFrame() || EscapedThisFrame()))
                SetHairView(false);
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
    // 画面の切り替え
    // ------------------------------------------------------------------

    void SetHairView(bool on)
    {
        _inHairView = on;

        if (_hairRoot != null) _hairRoot.gameObject.SetActive(on);
        if (on) AlignHairCameraToMain();
        if (_hairCam != null) _hairCam.enabled = on;

        // メインカメラは GameObject ごと止めない。AudioListener が載っていることが多く、
        // 一緒に無効化すると毛の声が鳴らなくなるため、Camera コンポーネントだけを切り替える。
        if (_mainCam != null) _mainCam.enabled = !on;

        if (on)
        {
            // 切り替え直後に一声。以降はランダムな間隔で。
            _nextVoiceAt = Time.time + 1.2f;
        }
        else if (_voice != null && _voice.isPlaying)
        {
            _voice.Stop();
            _speakT = -1f;
        }
    }

    /// <summary>
    /// 人が椅子に座っているビューのカメラ位置・向き・画角を、そのまま毛の側へ移す。
    /// 人を基準にした相対位置を、毛の場（頭皮の地表）を基準に置き直すだけなので、
    /// 二つの画面は同じ構図になり、切り替えが「同じ椅子に別のものが座っている」入れ替えとして読める。
    /// </summary>
    void AlignHairCameraToMain()
    {
        if (!matchMainCameraFraming || _hairCam == null || _hairRoot == null) return;
        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        Transform subject = _subjectRoot;
        if (subject == null) return;

        Vector3 rel = subject.InverseTransformPoint(_mainCam.transform.position);
        Quaternion relRot = Quaternion.Inverse(subject.rotation) * _mainCam.transform.rotation;

        _hairCam.transform.position = _hairRoot.TransformPoint(rel);
        _hairCam.transform.rotation = _hairRoot.rotation * relRot;
        _hairCam.fieldOfView = _mainCam.fieldOfView;
    }

    void UpdateHairView()
    {
        AlignHairCameraToMain();

        if (Time.time >= _nextVoiceAt)
        {
            Speak();
            _nextVoiceAt = Time.time + Random.Range(voiceIntervalMin, voiceIntervalMax);
        }

        // 発声中はわずかに伸び縮みさせ、声の主が誰なのかを画面上で結びつける
        if (_strand != null)
        {
            float wobble = 0f;
            if (_speakT >= 0f)
            {
                _speakT += Time.deltaTime;
                float dur = (_voice != null && _voice.clip != null) ? _voice.clip.length : 1.2f;
                if (_speakT > dur) _speakT = -1f;
                else wobble = Mathf.Sin(_speakT * 18f) * 0.06f * Mathf.Clamp01((dur - _speakT) / dur);
            }
            float breathe = Mathf.Sin(Time.time * 1.4f) * 0.012f;
            _strand.localRotation = Quaternion.Euler(0f, 0f, (wobble + breathe) * 60f);
        }
    }

    void Speak()
    {
        if (_voice == null || _voice.clip == null) return;
        _voice.volume = voiceVolume;
        _voice.Play();
        _speakT = 0f;
    }

    // ------------------------------------------------------------------
    // 毛の画面をコードで組み立てる
    // ------------------------------------------------------------------

    void BuildHairScene()
    {
        var root = new GameObject("HairChairView").transform;
        root.SetParent(transform, false);
        root.position = hairSceneOrigin;
        _hairRoot = root;

        var camGo = new GameObject("HairCamera");
        camGo.transform.SetParent(root, false);
        // ほぼ真横から。座っている輪郭は側面でこそ読める。
        camGo.transform.localPosition = new Vector3(2.35f, 0.85f, -1.15f);
        camGo.transform.localRotation = Quaternion.LookRotation(
            (new Vector3(0f, 0.55f, -0.2f) - camGo.transform.localPosition).normalized, Vector3.up);
        _hairCam = camGo.AddComponent<Camera>();
        _hairCam.clearFlags = CameraClearFlags.SolidColor;
        _hairCam.backgroundColor = backgroundColor;
        _hairCam.fieldOfView = 45f;
        _hairCam.nearClipPlane = 0.05f;
        _hairCam.farClipPlane = 100f;

        var lightGo = new GameObject("HairLight");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localRotation = Quaternion.Euler(38f, -28f, 0f);
        var lt = lightGo.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.1f;

        var scalpMat = MakeMaterial(scalpColor);
        var hairMat = MakeMaterial(hairColor);
        var chairMat = MakeMaterial(chairColor);

        // 頭皮 —— 巨大な球の一部として、地面のように広がる
        var scalp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        scalp.name = "Scalp";
        scalp.transform.SetParent(root, false);
        scalp.transform.localScale = Vector3.one * 40f;
        scalp.transform.localPosition = new Vector3(0f, -20f, 0f); // 上端だけが地表として見える
        StripCollider(scalp);
        scalp.GetComponent<MeshRenderer>().sharedMaterial = scalpMat;

        // 椅子
        BuildChair(root, chairMat);

        // 毛 —— 頭皮から生え、腰かけた姿勢に折れ曲がる一本
        _strand = BuildSeatedStrand(root, hairMat);

        // 声 —— 毛の画面を消しても再生を制御できるよう、この GameObject 側に持たせる
        _voice = gameObject.AddComponent<AudioSource>();
        _voice.playOnAwake = false;
        _voice.spatialBlend = 0f;   // 2D。画面いっぱいの毛の声なので距離減衰させない
        _voice.clip = voiceClip;
    }

    void BuildChair(Transform root, Material mat)
    {
        var chair = new GameObject("Chair").transform;
        chair.SetParent(root, false);

        MakeBox(chair, "Seat", new Vector3(0f, 0.42f, -0.10f), new Vector3(0.70f, 0.06f, 0.60f), mat);
        MakeBox(chair, "Backrest", new Vector3(0f, 0.75f, 0.17f), new Vector3(0.70f, 0.70f, 0.06f), mat);
        MakeBox(chair, "LegFL", new Vector3(-0.30f, 0.21f, -0.34f), new Vector3(0.06f, 0.42f, 0.06f), mat);
        MakeBox(chair, "LegFR", new Vector3(0.30f, 0.21f, -0.34f), new Vector3(0.06f, 0.42f, 0.06f), mat);
        MakeBox(chair, "LegBL", new Vector3(-0.30f, 0.21f, 0.14f), new Vector3(0.06f, 0.42f, 0.06f), mat);
        MakeBox(chair, "LegBR", new Vector3(0.30f, 0.21f, 0.14f), new Vector3(0.06f, 0.42f, 0.06f), mat);
    }

    /// <summary>
    /// 毛が「腰かけている」形。一本の毛を一続きの折れ線として組む。
    ///
    /// 頭皮（地面）から生えた根元がそのまま脚になり、膝で後ろへ折れて座面に載り、
    /// 腰から立ち上がって背もたれに凭れ、先端が頭になる。
    /// 根元＝足であることが、毛が生えていることと座っていることを同時に成立させる。
    /// </summary>
    Transform BuildSeatedStrand(Transform root, Material mat)
    {
        var strand = new GameObject("Strand").transform;
        strand.SetParent(root, false);
        strand.localPosition = Vector3.zero;   // 頭皮の地表と同じ高さを基準にする

        // 折れ線の節（strand ローカル / y=0 が頭皮の地表、椅子の座面は y=0.45）
        Vector3 foot     = new Vector3(0f, 0.02f, -0.46f);  // 根元＝頭皮から生えている足元
        Vector3 knee     = new Vector3(0f, 0.46f, -0.44f);  // 膝
        Vector3 hip      = new Vector3(0f, 0.48f, -0.04f);  // 腰（座面のうえ）
        Vector3 shoulder = new Vector3(0f, 0.98f,  0.09f);  // 肩（背もたれに凭れる）
        Vector3 tip      = new Vector3(0f, 1.16f,  0.04f);  // 先端＝頭

        const float thick = 0.055f;
        MakeSegment(strand, "Shin",     foot,     knee,     thick * 0.92f, mat);
        MakeSegment(strand, "Thigh",    knee,     hip,      thick,         mat);
        MakeSegment(strand, "Back",     hip,      shoulder, thick,         mat);
        MakeSegment(strand, "Neck",     shoulder, tip,      thick * 0.95f, mat);

        // 関節を球で埋め、折れ目が角張らないようにする
        MakeJoint(strand, "JointKnee",     knee,     thick,         mat);
        MakeJoint(strand, "JointHip",      hip,      thick,         mat);
        MakeJoint(strand, "JointShoulder", shoulder, thick,         mat);
        MakeJoint(strand, "Tip",           tip,      thick * 1.15f, mat);

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
        float len = dir.magnitude;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        go.transform.localScale = new Vector3(thickness, len * 0.5f, thickness); // Cylinder は高さ2
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
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
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
