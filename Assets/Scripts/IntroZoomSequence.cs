using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 冒頭ムービー：チャールズ&レイ・イームズ「Powers of Ten」型の連続ズーム演出。
///
///   宇宙 → 地球 → 島 → 街 → 建物 → 部屋の中の人 → （ゲーム開始）
///
/// 既存スクリプトとのコンフリクトを避けるため、このファイル単体で完結する新規スクリプト。
/// 外部アセットは一切不要で、地球のテクスチャ・島・街・建物・人物まですべてコードで手続き的に生成する。
///
/// ■ 設計上の要点
/// 実スケール（地球 1.2e7 m ～ 人 1.8 m）をそのまま扱うと float 精度が破綻するため、
/// 「カメラは常に原点で静止し、各ステージが指数関数的に巨大化しながらカメラへ迫る」方式を採る。
/// ステージの切り替わりは大気・靄のフラッシュで覆って隠すため、段差が見えず連続した降下に見える。
/// これは Powers of Ten が実写とアニメを切り替えていた手つきの、リアルタイム版の翻案にあたる。
///
/// ■ 使い方
/// 空の GameObject にアタッチして Play するだけ。カメラは自動生成/自動検出される。
/// 演出後に別シーンへ遷移したい場合は nextSceneName を設定するか、onComplete にハンドラを登録する。
/// 実行中に任意キー / クリックでスキップ可能。
/// </summary>
public class IntroZoomSequence : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------

    [Header("再生")]
    [Tooltip("Play と同時に自動再生する")]
    public bool playOnStart = true;
    [Tooltip("全体の再生速度倍率（大きいほど速い）")]
    public float speed = 1f;
    [Tooltip("任意キー / クリックでスキップ可能にする")]
    public bool allowSkip = true;

    [Header("完了時")]
    [Tooltip("演出後に読み込むシーン名（空ならシーン遷移しない）")]
    public string nextSceneName = "";
    public UnityEvent onComplete;

    [Header("見た目")]
    [Tooltip("ステージを配置するカメラ前方の距離")]
    public float anchorDistance = 30f;
    [Tooltip("Powers of Ten 風のスケール表示を出す")]
    public bool showScaleCaption = true;
    public int captionFontSize = 22;
    [Tooltip("演出用に環境光を調整する（既定のスカイボックス光は色が白く飛ぶため）")]
    public bool configureLighting = true;

    [Header("カメラ（未設定なら自動生成/自動検出）")]
    public Camera introCamera;

    // ------------------------------------------------------------------
    // ステージ定義
    // ------------------------------------------------------------------

    class Stage
    {
        public string caption;        // 画面に出す「10^n m」表記
        public string label;          // 対象名
        public float duration;        // 秒
        public float startScale;      // 開始時の見かけスケール
        public float endScale;        // 終了時（カメラを覆い尽くす）
        public Color hazeColor;       // 次ステージへの受け渡しを覆う靄の色
        public bool skipHaze;         // 靄を挟まず、そのまま次へ繋ぐ
        public Color skyColor;        // このステージでの背景（空）の色
        public Transform root;        // 生成済みの実体
        public Transform backdrop;    // 海や地面など、見かけの大きさを保ちたい背景面
        public float backdropSize;    // その背景面のワールド上の見かけサイズ
        public System.Action<float> onUpdate; // ステージ固有の追加演出（進行度 0-1）
    }

    readonly List<Stage> _stages = new List<Stage>();

    // ------------------------------------------------------------------
    // 内部状態
    // ------------------------------------------------------------------

    Transform _anchor;
    int _index = -1;
    float _t;            // 現ステージ内の経過秒
    bool _playing;
    bool _finished;
    float _haze;         // 0-1 画面を覆う靄
    Color _hazeColor = Color.black;
    Texture2D _fillTex;
    GUIStyle _capStyle, _labelStyle;

    Transform _starField;
    Transform _earth;
    Transform _person;
    Transform _seaBackdrop;
    Transform _cityGround;

    // ------------------------------------------------------------------

    void Awake()
    {
        _fillTex = new Texture2D(1, 1);
        _fillTex.SetPixel(0, 0, Color.white);
        _fillTex.Apply();

        ConfigureLighting();
        EnsureCamera();
        BuildAnchor();
        BuildStages();
        HideAllStages();
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    public void Play()
    {
        // 途中から再生し直しても前回のステージが残らないよう、必ず全消しから始める
        HideAllStages();
        if (_starField != null)
        {
            _starField.gameObject.SetActive(true);
            SetGroupAlpha(_starField, 1f);
        }
        if (introCamera != null) introCamera.backgroundColor = Color.black;

        _haze = 0f;
        _finished = false;
        _playing = true;
        _index = -1;
        AdvanceStage();
    }

    // ------------------------------------------------------------------
    // セットアップ
    // ------------------------------------------------------------------

    /// <summary>
    /// 既定のスカイボックス由来の環境光は全体を白く飛ばしてしまい、海の深い青も緑の島も
    /// 淡く濁ってしまう。演出用のシーンなので、環境光を明示的に落として色を締める。
    /// </summary>
    void ConfigureLighting()
    {
        if (!configureLighting) return;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.24f, 0.26f, 0.30f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
    }

    void EnsureCamera()
    {
        if (introCamera == null) introCamera = GetComponent<Camera>();
        if (introCamera == null) introCamera = Camera.main;
        if (introCamera == null)
        {
            var go = new GameObject("IntroCamera");
            go.transform.SetParent(transform, false);
            introCamera = go.AddComponent<Camera>();
        }

        introCamera.clearFlags = CameraClearFlags.SolidColor;
        introCamera.backgroundColor = Color.black;
        introCamera.nearClipPlane = 0.05f;
        introCamera.farClipPlane = 5000f;
        introCamera.fieldOfView = 60f;
    }

    void BuildAnchor()
    {
        var go = new GameObject("IntroStages");
        go.transform.SetParent(transform, false);
        _anchor = go.transform;
        _anchor.position = introCamera.transform.position + introCamera.transform.forward * anchorDistance;
        _anchor.rotation = Quaternion.identity;
    }

    void BuildStages()
    {
        // 1. 宇宙 —— 星々のなか、遠くに地球が点として浮かぶ
        _starField = BuildStarField();
        _stages.Add(new Stage
        {
            caption = "10^9 m",
            label = "SPACE",
            duration = 5f,
            startScale = 0.004f,
            endScale = 0.35f,
            hazeColor = new Color(0.02f, 0.03f, 0.08f),
            // 宇宙→地球は同じ地球を続けて見せるだけで断絶がない。靄を挟むとかえって邪魔になる。
            skipHaze = true,
            skyColor = Color.black,
            root = _earth = BuildEarth(),
        });

        // 2. 地球 —— 青い球が視界いっぱいに育ち、大気圏へ落ちてゆく
        _stages.Add(new Stage
        {
            caption = "10^7 m",
            label = "EARTH",
            duration = 6f,
            startScale = 0.35f,
            endScale = 26f,
            hazeColor = new Color(0.72f, 0.82f, 0.95f), // 雲を突き抜ける白
            skyColor = Color.black,
            root = _earth,
            onUpdate = t =>
            {
                if (_starField != null)
                {
                    // 地球が視界を占め、大気に入ってゆく終盤で星が消えてゆく
                    SetGroupAlpha(_starField, 1f - Mathf.Clamp01((t - 0.55f) / 0.3f));
                }
            }
        });

        // 3. 島 —— 海に浮かぶ島影が見えてくる
        _stages.Add(new Stage
        {
            caption = "10^5 m",
            label = "ISLAND",
            duration = 5.5f,
            startScale = 0.06f,
            endScale = 24f,
            hazeColor = new Color(0.80f, 0.86f, 0.92f),
            skyColor = new Color(0.55f, 0.72f, 0.88f),
            root = BuildIsland(),
            backdrop = _seaBackdrop,
            backdropSize = 170f,
        });

        // 4. 街 —— 島の上に建物の群れが立ち上がる
        _stages.Add(new Stage
        {
            caption = "10^3 m",
            label = "CITY",
            duration = 5.5f,
            startScale = 0.07f,
            endScale = 20f,
            hazeColor = new Color(0.85f, 0.86f, 0.86f),
            skyColor = new Color(0.62f, 0.76f, 0.89f),
            root = BuildCity(),
            backdrop = _cityGround,
            backdropSize = 150f,
        });

        // 5. 建物 —— 一棟に絞り込み、窓の格子が見えてくる
        _stages.Add(new Stage
        {
            caption = "10^1 m",
            label = "BUILDING",
            duration = 5f,
            startScale = 0.55f,
            endScale = 26f,
            hazeColor = new Color(0.9f, 0.9f, 0.9f),
            skyColor = new Color(0.72f, 0.82f, 0.90f),
            root = BuildBuilding(),
        });

        // 6. 人 —— 部屋の中の一人へ。最後は頭部＝この game の舞台へ寄る
        _stages.Add(new Stage
        {
            caption = "10^0 m",
            label = "A PERSON",
            duration = 6f,
            startScale = 0.5f,
            endScale = 34f,
            hazeColor = new Color(1f, 1f, 1f),
            skyColor = Color.white,
            root = BuildRoomAndPerson(),
            onUpdate = t =>
            {
                // 最後は人物の頭部が画面中心に来るよう、寄りながら少しだけ上へパンする
                // （頭部は人物ローカルで y=+0.12 にあるので、その分だけ下げると中心に来る）
                if (_person != null)
                    _person.localPosition = new Vector3(0f, Mathf.Lerp(0f, -0.12f, EaseInOut(t)), 0f);
            }
        });
    }

    void HideAllStages()
    {
        for (int i = 0; i < _stages.Count; i++)
            if (_stages[i].root != null) _stages[i].root.gameObject.SetActive(false);
        if (_starField != null) _starField.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------
    // 進行
    // ------------------------------------------------------------------

    void Update()
    {
        if (allowSkip && _playing && AnyInputThisFrame()) { Skip(); return; }
        if (!_playing || _index < 0 || _index >= _stages.Count) return;

        Stage s = _stages[_index];
        float dt = Time.deltaTime * Mathf.Max(0.01f, speed);
        _t += dt;

        float u = Mathf.Clamp01(_t / s.duration);

        // 指数補間＝等速の「降下感」。線形だと最後に急激に速く見えてしまう。
        float scale = Mathf.Exp(Mathf.Lerp(Mathf.Log(s.startScale), Mathf.Log(s.endScale), u));
        if (s.root != null) s.root.localScale = Vector3.one * scale;

        // 海や地面は「見かけの大きさ」を保つ。地面まで一緒に拡大すると視界から外れ、
        // 逆に拡大しないと降下している感じが出ないため、背景面だけ逆スケールで打ち消す。
        if (s.backdrop != null)
            s.backdrop.localScale = Vector3.one * (s.backdropSize / Mathf.Max(0.0001f, scale));

        if (introCamera != null)
            introCamera.backgroundColor = Color.Lerp(introCamera.backgroundColor, s.skyColor, dt * 2.5f);

        s.onUpdate?.Invoke(u);

        // 地球はゆっくり自転させる
        if (_earth != null && _earth.gameObject.activeSelf)
            _earth.Rotate(Vector3.up, dt * 3.5f, Space.Self);

        // ステージ終端で靄を立ち上げ、切り替わりを覆い隠す
        const float hazeIn = 0.86f;
        if (s.skipHaze)
            _haze = Mathf.Max(0f, _haze - dt * 2.2f);
        else
            _haze = u > hazeIn ? Mathf.InverseLerp(hazeIn, 1f, u) : Mathf.Max(0f, _haze - dt * 2.2f);
        _hazeColor = s.hazeColor;

        if (u >= 1f) AdvanceStage();
    }

    void AdvanceStage()
    {
        if (_index >= 0 && _index < _stages.Count)
        {
            var prev = _stages[_index];
            // 同じ実体を続けて使うステージ（宇宙→地球）では消さない
            bool reused = _index + 1 < _stages.Count && _stages[_index + 1].root == prev.root;
            if (!reused && prev.root != null) prev.root.gameObject.SetActive(false);
        }

        _index++;
        _t = 0f;

        if (_index >= _stages.Count) { Finish(); return; }

        var s = _stages[_index];
        if (s.root != null)
        {
            s.root.gameObject.SetActive(true);
            s.root.localScale = Vector3.one * s.startScale;
        }
    }

    public void Skip()
    {
        _playing = false;
        for (int i = 0; i < _stages.Count; i++)
            if (_stages[i].root != null) _stages[i].root.gameObject.SetActive(false);
        Finish();
    }

    void Finish()
    {
        if (_finished) return;
        _finished = true;
        _playing = false;
        _haze = 0f;

        onComplete?.Invoke();

        if (!string.IsNullOrEmpty(nextSceneName))
            SceneManager.LoadScene(nextSceneName);
    }

    // ------------------------------------------------------------------
    // ステージの実体をコードで組み立てる
    // ------------------------------------------------------------------

    Transform BuildStarField()
    {
        var go = new GameObject("Stage_Stars");
        go.transform.SetParent(_anchor, false);

        // 1 メッシュにまとめた板ポリの星。カメラは静止しているので常にこちらを向く。
        const int count = 700;
        var verts = new Vector3[count * 4];
        var tris = new int[count * 6];
        var cols = new Color[count * 4];

        Vector3 right = Vector3.right, up = Vector3.up;
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            if (dir.z < 0f) dir.z = -dir.z;     // カメラの前方（+Z）側の半球へ寄せる
            float dist = Random.Range(300f, 900f);
            Vector3 c = dir * dist;
            // 見かけの大きさが距離で消えないよう、距離に比例させる
            float s = dist * Random.Range(0.0016f, 0.0055f);
            float b = Random.Range(0.45f, 1f);

            int v = i * 4;
            verts[v + 0] = c - right * s - up * s;
            verts[v + 1] = c + right * s - up * s;
            verts[v + 2] = c + right * s + up * s;
            verts[v + 3] = c - right * s + up * s;

            Color col = new Color(b, b, Mathf.Min(1f, b * 1.15f), 1f);
            cols[v + 0] = cols[v + 1] = cols[v + 2] = cols[v + 3] = col;

            int t = i * 6;
            tris[t + 0] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }

        var mesh = new Mesh { name = "StarField" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = MakeMaterial(Color.white, lit: false, transparent: true);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 星は距離が固定なので、ステージのスケールに引きずられないよう anchor 直下で等倍に保つ
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    Transform BuildEarth()
    {
        var root = new GameObject("Stage_Earth").transform;
        root.SetParent(_anchor, false);

        var globe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        globe.name = "Globe";
        globe.transform.SetParent(root, false);
        StripCollider(globe);

        var mat = MakeMaterial(Color.white, lit: true, transparent: false);
        SetTexture(mat, BuildEarthTexture(1024, 512));
        SetSmoothness(mat, 0.25f);
        globe.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // 薄い大気の縁
        var atmo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        atmo.name = "Atmosphere";
        atmo.transform.SetParent(root, false);
        atmo.transform.localScale = Vector3.one * 1.035f;
        StripCollider(atmo);
        atmo.GetComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(0.45f, 0.7f, 1f, 0.18f), lit: false, transparent: true);

        // 太陽光（このステージ用の簡易ライト）
        var lightGo = new GameObject("SunLight");
        lightGo.transform.SetParent(root, false);
        var lt = lightGo.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.15f;
        lt.color = new Color(1f, 0.97f, 0.9f);
        lightGo.transform.rotation = Quaternion.Euler(18f, -35f, 0f);

        return root;
    }

    Texture2D BuildEarthTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;

        Color ocean = new Color(0.06f, 0.20f, 0.44f);
        Color oceanDeep = new Color(0.03f, 0.12f, 0.32f);
        Color land = new Color(0.20f, 0.42f, 0.18f);
        Color sand = new Color(0.62f, 0.55f, 0.34f);
        Color ice = new Color(0.93f, 0.95f, 0.97f);

        float ox = Random.Range(0f, 500f), oy = Random.Range(0f, 500f);
        var px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            float lat = Mathf.Abs(v - 0.5f) * 2f;    // 0=赤道 1=極
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);

                // 経度方向に継ぎ目が出ないよう、円筒座標をノイズ座標に使う
                float ang = u * Mathf.PI * 2f;
                float nx = Mathf.Cos(ang) * 1.6f + ox;
                float nz = Mathf.Sin(ang) * 1.6f + oy;
                float ny = v * 3.2f;

                float n = FBm(nx, ny, nz, 5);
                // 多重ノイズの平均は 0.5 付近へ収束してしまうため、明示的にコントラストを引き伸ばす
                n = Mathf.Clamp01((n - 0.5f) * 3.4f + 0.5f);
                n -= lat * 0.16f;                    // 極側はやや海に

                Color c;
                if (n > 0.56f) c = Color.Lerp(land, sand, Mathf.InverseLerp(0.56f, 0.85f, n) * 0.45f);
                else if (n > 0.52f) c = sand;
                else c = Color.Lerp(oceanDeep, ocean, Mathf.InverseLerp(0.20f, 0.52f, n));

                if (lat > 0.86f) c = Color.Lerp(c, ice, Mathf.InverseLerp(0.86f, 0.97f, lat));

                px[y * w + x] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    Transform BuildIsland()
    {
        var root = new GameObject("Stage_Island").transform;
        root.SetParent(_anchor, false);
        // 真上からの俯瞰。Powers of Ten の垂直降下に倣い、水平面を正面から見る向きにする。
        root.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        // 海（見かけの大きさを保つ背景面。localScale は Update 側で制御する）
        var sea = GameObject.CreatePrimitive(PrimitiveType.Quad);
        sea.name = "Sea";
        sea.transform.SetParent(root, false);
        // Unity の Quad は法線が -Z 向きのため、+90 度で水平（ローカル +Y 向き）になる
        sea.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        sea.transform.localPosition = new Vector3(0f, -0.05f, 0f);
        StripCollider(sea);
        var seaMat = MakeMaterial(new Color(0.10f, 0.28f, 0.50f), lit: true, transparent: false);
        SetSmoothness(seaMat, 0.05f);
        sea.GetComponent<MeshRenderer>().sharedMaterial = seaMat;
        _seaBackdrop = sea.transform;

        var blob = BuildBlobMesh(1f, 72, 0.34f, Random.Range(0f, 100f));

        // 砂浜（島より一回り大きい同型を下に敷く）
        var beach = new GameObject("Beach");
        beach.transform.SetParent(root, false);
        beach.transform.localPosition = new Vector3(0f, 0.01f, 0f);
        beach.transform.localScale = Vector3.one * 8.6f;
        beach.AddComponent<MeshFilter>().sharedMesh = blob;
        beach.AddComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(0.78f, 0.72f, 0.52f), lit: true, transparent: false);

        // 島影（ノイズで縁を崩した円盤）
        var island = new GameObject("Island");
        island.transform.SetParent(root, false);
        island.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        island.transform.localScale = Vector3.one * 8f;
        island.AddComponent<MeshFilter>().sharedMesh = blob;
        island.AddComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(0.28f, 0.45f, 0.23f), lit: true, transparent: false);

        // 街のある位置を示す小さな灰色の染み（次のステージへの視線誘導）
        var town = new GameObject("TownPatch");
        town.transform.SetParent(root, false);
        town.transform.localPosition = new Vector3(0.8f, 0.06f, 0.5f);
        town.transform.localScale = Vector3.one * 1.5f;
        town.AddComponent<MeshFilter>().sharedMesh = BuildBlobMesh(1f, 32, 0.25f, Random.Range(0f, 100f));
        town.AddComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(0.56f, 0.55f, 0.53f), lit: true, transparent: false);

        AddStageLight(root, new Vector3(45f, -20f, 0f), 1.05f);
        return root;
    }

    Transform BuildCity()
    {
        var root = new GameObject("Stage_City").transform;
        root.SetParent(_anchor, false);
        // ほぼ真上から。わずかに傾けて建物の側面＝高さを読ませる。
        root.localRotation = Quaternion.Euler(-72f, 16f, 0f);

        // 地面（見かけの大きさを保つ背景面。localScale は Update 側で制御する）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localPosition = new Vector3(0f, -0.005f, 0f);
        StripCollider(ground);
        ground.GetComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(0.44f, 0.46f, 0.42f), lit: true, transparent: false);
        _cityGround = ground.transform;

        var wallMat = MakeMaterial(new Color(0.72f, 0.72f, 0.70f), lit: true, transparent: false);
        var wallMat2 = MakeMaterial(new Color(0.58f, 0.60f, 0.63f), lit: true, transparent: false);
        var roadMat = MakeMaterial(new Color(0.26f, 0.26f, 0.27f), lit: true, transparent: false);

        const int n = 13;             // n x n ブロック
        const float step = 0.85f;
        float half = (n - 1) * step * 0.5f;

        for (int ix = 0; ix < n; ix++)
        {
            for (int iz = 0; iz < n; iz++)
            {
                float x = ix * step - half;
                float z = iz * step - half;

                // 3 ブロックごとに道路を通す
                bool road = (ix % 3 == 0) || (iz % 3 == 0);
                if (road)
                {
                    var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    r.transform.SetParent(root, false);
                    r.transform.localPosition = new Vector3(x, 0.01f, z);
                    r.transform.localScale = new Vector3(step * 0.95f, 0.02f, step * 0.95f);
                    StripCollider(r);
                    r.GetComponent<MeshRenderer>().sharedMaterial = roadMat;
                    continue;
                }

                // 中心に近いほど高い＝都心のシルエット
                float d = Mathf.Clamp01(1f - new Vector2(x, z).magnitude / (half * 1.15f));
                float hgt = Mathf.Lerp(0.25f, 2.6f, d * Random.Range(0.35f, 1f));

                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Building";
                b.transform.SetParent(root, false);
                b.transform.localScale = new Vector3(step * 0.62f, hgt, step * 0.62f);
                b.transform.localPosition = new Vector3(x, hgt * 0.5f, z);
                StripCollider(b);
                b.GetComponent<MeshRenderer>().sharedMaterial = Random.value > 0.5f ? wallMat : wallMat2;
            }
        }

        AddStageLight(root, new Vector3(48f, -30f, 0f), 1.1f);
        return root;
    }

    Transform BuildBuilding()
    {
        var root = new GameObject("Stage_Building").transform;
        root.SetParent(_anchor, false);
        // 地面が巨大なため、わずかでも前後に傾けると遠端が空を覆ってしまう。傾けるのは水平回転のみ。
        root.localRotation = Quaternion.Euler(0f, 22f, 0f);

        // 地面（建物が宙に浮かないよう、足元を受ける）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localPosition = new Vector3(0f, -0.86f, 0f);
        ground.transform.localScale = Vector3.one * 60f;
        StripCollider(ground);
        ground.GetComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(0.34f, 0.35f, 0.33f), lit: true, transparent: false);

        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Tower";
        body.transform.SetParent(root, false);
        body.transform.localScale = new Vector3(0.85f, 1.7f, 0.75f);
        StripCollider(body);

        var mat = MakeMaterial(Color.white, lit: true, transparent: false);
        SetTexture(mat, BuildWindowTexture(256, 512));
        SetSmoothness(mat, 0.55f);
        body.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // 隣接する建物（奥行きを出す）
        var neighborMat = MakeMaterial(new Color(0.55f, 0.56f, 0.58f), lit: true, transparent: false);
        for (int i = 0; i < 5; i++)
        {
            var nb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nb.transform.SetParent(root, false);
            float h = Random.Range(0.7f, 1.5f);
            nb.transform.localScale = new Vector3(Random.Range(0.4f, 0.8f), h, Random.Range(0.4f, 0.8f));
            nb.transform.localPosition = new Vector3(Random.Range(-2.2f, 2.2f), -0.86f + h * 0.5f, Random.Range(0.9f, 2.4f));
            StripCollider(nb);
            nb.GetComponent<MeshRenderer>().sharedMaterial = neighborMat;
        }

        // これから入ってゆく一室だけ、明かりを灯しておく
        var win = GameObject.CreatePrimitive(PrimitiveType.Quad);
        win.name = "LitWindow";
        win.transform.SetParent(root, false);
        win.transform.localPosition = new Vector3(0.06f, 0.10f, -0.381f);
        win.transform.localScale = new Vector3(0.10f, 0.07f, 1f);
        StripCollider(win);
        win.GetComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(1f, 0.93f, 0.72f), lit: false, transparent: false);

        AddStageLight(root, new Vector3(28f, -25f, 0f), 1.0f);
        return root;
    }

    Transform BuildRoomAndPerson()
    {
        var root = new GameObject("Stage_Room").transform;
        root.SetParent(_anchor, false);

        // 部屋（床のみ。背景に壁を立てると人物の輪郭が埋もれるため、奥は抜いておく）
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(root, false);
        floor.transform.localScale = new Vector3(4f, 0.1f, 3f);
        floor.transform.localPosition = new Vector3(0f, -1.1f, 0f);
        StripCollider(floor);
        floor.GetComponent<MeshRenderer>().sharedMaterial =
            MakeMaterial(new Color(0.55f, 0.45f, 0.36f), lit: true, transparent: false);

        // 人物（プリミティブの組み合わせ）
        var person = new GameObject("Person").transform;
        person.SetParent(root, false);
        _person = person;

        var skin = MakeMaterial(new Color(0.93f, 0.80f, 0.70f), lit: true, transparent: false);
        var cloth = MakeMaterial(new Color(0.30f, 0.38f, 0.52f), lit: true, transparent: false);

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(person, false);
        body.transform.localScale = new Vector3(0.42f, 0.45f, 0.30f);
        body.transform.localPosition = new Vector3(0f, -0.45f, 0f);
        StripCollider(body);
        body.GetComponent<MeshRenderer>().sharedMaterial = cloth;

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(person, false);
        head.transform.localScale = new Vector3(0.34f, 0.40f, 0.34f);
        head.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        StripCollider(head);
        head.GetComponent<MeshRenderer>().sharedMaterial = skin;

        // 頭髪 —— このゲームの主題そのもの。最後にここへ辿り着く。
        // 残る一本。頭頂からわずかに傾けて立たせ、シルエットで読めるようにする。
        var hairMat = MakeMaterial(new Color(0.12f, 0.10f, 0.09f), lit: true, transparent: false);
        var hair = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hair.name = "Hair";
        hair.transform.SetParent(head.transform, false);

        Vector3 hairDir = new Vector3(0.16f, 1f, 0.06f).normalized;
        hair.transform.localPosition = hairDir * 0.5f;
        hair.transform.localRotation = Quaternion.FromToRotation(Vector3.up, hairDir);
        hair.transform.localScale = new Vector3(0.035f, 0.30f, 0.035f);
        StripCollider(hair);
        hair.GetComponent<MeshRenderer>().sharedMaterial = hairMat;

        AddStageLight(root, new Vector3(35f, -18f, 0f), 1.05f);
        return root;
    }

    // ------------------------------------------------------------------
    // 生成ヘルパー
    // ------------------------------------------------------------------

    /// <summary>
    /// ノイズで縁を崩した円盤メッシュ（島・市街地の染みに使う）。
    /// XZ 平面に水平に寝かせた状態で生成し、法線は上向き。
    /// 裏返って消えることがないよう表裏の三角形を両方張る。
    /// </summary>
    Mesh BuildBlobMesh(float radius, int segments, float irregularity, float seed)
    {
        var verts = new Vector3[segments + 1];
        var normals = new Vector3[segments + 1];
        var tris = new int[segments * 6];

        verts[0] = Vector3.zero;
        normals[0] = Vector3.up;

        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            float n = Mathf.PerlinNoise(Mathf.Cos(a) * 1.4f + seed, Mathf.Sin(a) * 1.4f + seed);
            float r = radius * (1f - irregularity * 0.5f + n * irregularity);
            verts[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            normals[i + 1] = Vector3.up;

            int cur = i + 1;
            int next = (i + 1) % segments + 1;
            int t = i * 6;
            tris[t + 0] = 0; tris[t + 1] = next; tris[t + 2] = cur;   // 表
            tris[t + 3] = 0; tris[t + 4] = cur;  tris[t + 5] = next;  // 裏
        }

        var mesh = new Mesh { name = "Blob" };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>窓の格子模様。数室だけ明かりが点いている。</summary>
    Texture2D BuildWindowTexture(int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color[w * h];

        Color wall = new Color(0.62f, 0.63f, 0.65f);
        Color glass = new Color(0.16f, 0.22f, 0.28f);
        Color litGlass = new Color(1f, 0.90f, 0.66f);

        const int cols = 6, rows = 16;
        float cw = (float)w / cols, ch = (float)h / rows;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float fx = (x % cw) / cw;
                float fy = (y % ch) / ch;
                bool isWindow = fx > 0.22f && fx < 0.78f && fy > 0.25f && fy < 0.75f;

                Color c = wall;
                if (isWindow)
                {
                    int cell = (int)(y / ch) * cols + (int)(x / cw);
                    // 決定的な擬似乱数で、点灯する部屋をばらつかせる
                    float r = Frac(Mathf.Sin(cell * 12.9898f) * 43758.5453f);
                    c = r > 0.78f ? litGlass : glass;
                }
                px[y * w + x] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    void AddStageLight(Transform parent, Vector3 euler, float intensity)
    {
        var go = new GameObject("StageLight");
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(euler);
        var lt = go.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = intensity;
        lt.color = new Color(1f, 0.98f, 0.94f);
    }

    Material MakeMaterial(Color color, bool lit, bool transparent)
    {
        Shader sh = lit
            ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            : (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));

        var mat = new Material(sh);
        SetColor(mat, color);

        if (transparent)
        {
            // URP の Lit/Unlit を実行時に半透明へ切り替える
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return mat;
    }

    static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    static void SetTexture(Material m, Texture t)
    {
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
    }

    static void SetSmoothness(Material m, float v)
    {
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", v);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", v);
    }

    static void StripCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    void SetGroupAlpha(Transform root, float a)
    {
        var renderers = root.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i].sharedMaterial;
            if (m == null) continue;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                    : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            c.a = a;
            SetColor(m, c);
        }
    }

    // ------------------------------------------------------------------
    // 画面表示
    // ------------------------------------------------------------------

    void OnGUI()
    {
        EnsureStyles();

        // ステージ間を覆う靄
        if (_haze > 0.001f)
        {
            Color prev = GUI.color;
            Color c = _hazeColor; c.a = Mathf.Clamp01(_haze);
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _fillTex);
            GUI.color = prev;
        }

        if (!_playing || !showScaleCaption || _index < 0 || _index >= _stages.Count) return;

        var s = _stages[_index];
        float margin = 36f;
        float y = Screen.height - margin - captionFontSize * 2.6f;

        GUI.Label(new Rect(margin, y, 420f, captionFontSize * 1.6f), s.caption, _capStyle);
        GUI.Label(new Rect(margin, y + captionFontSize * 1.5f, 420f, captionFontSize * 1.4f), s.label, _labelStyle);

        if (allowSkip)
        {
            var skip = new GUIContent("PRESS ANY KEY TO SKIP");
            Vector2 sz = _labelStyle.CalcSize(skip);
            GUI.Label(new Rect(Screen.width - sz.x - margin, Screen.height - margin - sz.y, sz.x, sz.y), skip, _labelStyle);
        }
    }

    void EnsureStyles()
    {
        if (_capStyle == null)
            _capStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerLeft };
        if (_labelStyle == null)
            _labelStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Normal, alignment = TextAnchor.LowerLeft };

        _capStyle.fontSize = captionFontSize;
        _capStyle.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
        _labelStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(captionFontSize * 0.62f));
        _labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
    }

    // ------------------------------------------------------------------
    // ユーティリティ
    // ------------------------------------------------------------------

    static float EaseInOut(float t) => t * t * (3f - 2f * t);

    static float Frac(float v) => v - Mathf.Floor(v);

    static float FBm(float x, float y, float z, int octaves)
    {
        // Unity の PerlinNoise は 2D のみのため、3 平面を合成して擬似的な 3D ノイズにする
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            float n = (Mathf.PerlinNoise(x * freq, y * freq)
                     + Mathf.PerlinNoise(y * freq, z * freq)
                     + Mathf.PerlinNoise(z * freq, x * freq)) / 3f;
            sum += n * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }

    bool AnyInputThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
        return false;
#else
        return Input.anyKeyDown;
#endif
    }
}
