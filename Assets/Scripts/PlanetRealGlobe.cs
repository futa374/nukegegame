using UnityEngine;

/// <summary>
/// planet シーンの簡易な球を、実際の地球モデル（Assets/Models/EARTH.fbx）に差し替える。
///
/// このモデルは Surface（地表）/ Clouds（雲）/ Atom（大気）の三層でできている。
/// 地球がそれらしく見えるのは、大陸の形よりも、この三層の重なり方による。
/// 地表の上に雲が浮き、その外側に薄い大気の層がある——という順序と厚みが読めるだけで、
/// ただの球が惑星に見えはじめる。だからここでは、その三層をそれぞれ別の質感で扱っている。
///
/// テクスチャは同梱されていないため手続きで生成する。
/// ただし Inspector に差し込み口を用意してあるので、実写の地球テクスチャ（Blue Marble 等）を
/// 持っている場合はそれを入れれば、生成分より確実にリアルになる。
///
/// PlanetController（共同作業者の実装）には一切手を触れない。
/// 向こうが作った Globe と GlobeGrid を後から隠し、代わりにモデルを置くだけにしている。
/// </summary>
[DefaultExecutionOrder(100)]   // PlanetController が球を作り終えたあとに動く
public class PlanetRealGlobe : MonoBehaviour
{
    [Header("モデル")]
    [Tooltip("未設定なら Assets/Models/EARTH.fbx を読み込む")]
    public GameObject earthModel;
    [Tooltip("この半径になるよう、モデル全体を縮める")]
    public float globeRadius = 3f;
    [Tooltip("PlanetController の globeRadius に合わせる")]
    public bool matchPlanetController = true;

    [Header("差し替え")]
    [Tooltip("地球を出す。切ると簡易球もモデルも出ず、頭だけが残る（頭の造形を詰めるとき用）。")]
    public bool showEarth = true;
    [Tooltip("PlanetController が作る簡易球を隠す")]
    public bool hideProceduralGlobe = true;
    [Tooltip("緯線・経線を隠す（実写寄りにするなら消す）")]
    public bool hideGrid = true;

    [Header("自転")]
    public float spinDegPerSecond = 1.6f;
    [Tooltip("雲は地表よりわずかに速く流れる")]
    public float cloudExtraSpin = 0.9f;
    [Tooltip("地軸の傾き（度）")]
    public float axialTilt = 23.4f;

    [Header("テクスチャ（未設定なら生成する）")]
    public Texture2D earthAlbedo;
    public Texture2D cloudTexture;

    [Header("実際の雲を取ってくる")]
    [Tooltip("起動時に、いまの地球の雲を取得して貼る。\n取得できるまで（あるいは失敗したら）生成した雲のまま。")]
    public bool useLiveClouds = true;
    [Tooltip("live-cloud-maps が3時間ごとに更新している雲マップ（アルファ付きPNG）")]
    public string liveCloudUrl = "https://clouds.matteason.co.uk/images/4096x2048/clouds-alpha.png";
    [Tooltip("生成するテクスチャの横幅。縦はその半分。")]
    public int textureWidth = 2048;
    public int seed = 20260725;
    [Tooltip("UV が上下逆に貼られる場合に入れる")]
    public bool flipV = false;

    [Header("地表の色")]
    public Color oceanDeep = new Color(0.016f, 0.055f, 0.16f);
    public Color oceanShallow = new Color(0.05f, 0.22f, 0.38f);
    public Color shelf = new Color(0.10f, 0.38f, 0.50f);
    public Color forest = new Color(0.09f, 0.22f, 0.07f);
    public Color grass = new Color(0.20f, 0.30f, 0.10f);
    public Color desert = new Color(0.58f, 0.46f, 0.26f);
    public Color tundra = new Color(0.34f, 0.33f, 0.26f);
    public Color rock = new Color(0.35f, 0.31f, 0.27f);
    public Color ice = new Color(0.92f, 0.93f, 0.95f);

    [Header("出典表示")]
    [Tooltip("画面の隅にデータの出典を出す。EUMETSAT の利用条件で表記が義務づけられている。")]
    public bool showAttribution = true;
    public int attributionFontSize = 12;

    [Header("宇宙")]
    [Tooltip("背景を宇宙の色にする。白いままだと、地球ではなく地球儀に見えてしまう。")]
    public bool setSpaceBackground = true;
    public Color spaceColor = new Color(0.012f, 0.014f, 0.025f);

    [Header("層の間隔")]
    [Tooltip("雲の殻をどれだけ浮かせるか。低ポリ同士の潜り込みを防ぐ。")]
    public float cloudLift = 1.012f;
    [Tooltip("大気の殻をどれだけ浮かせるか")]
    public float atmosphereLift = 1.006f;

    [Header("雲と大気")]
    [Range(0f, 1f)] public float cloudCoverage = 0.30f;
    [Range(0f, 1f)] public float cloudOpacity = 0.45f;
    public Color atmosphereColor = new Color(0.28f, 0.52f, 0.95f);
    [Range(0f, 0.6f)] public float atmosphereOpacity = 0.06f;

    // ---- 内部 ----
    Transform _model, _surface, _clouds, _atom;
    bool _ready;
    float _retryT;

    void Start() { TryBuild(); }

    void Update()
    {
        if (!_ready)
        {
            // PlanetController の生成が遅れることがあるので、見つかるまで少しのあいだ待つ
            _retryT += Time.deltaTime;
            if (_retryT < 3f) TryBuild();
            return;
        }

        float dt = Time.deltaTime;
        if (_surface != null) _surface.Rotate(Vector3.up, spinDegPerSecond * dt, Space.Self);
        if (_clouds != null) _clouds.Rotate(Vector3.up, (spinDegPerSecond + cloudExtraSpin) * dt, Space.Self);
    }

    /// <summary>
    /// データの出典。EUMETSAT は "Contains modified EUMETSAT data" の表記を利用条件としている。
    /// 実際の観測を借りて絵にしている以上、それがどこから来たのかは画面に残しておく。
    /// </summary>
    void OnGUI()
    {
        if (!showAttribution || !showEarth) return;   // 地球を出していなければ出典も要らない

        if (_attributionStyle == null)
            _attributionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerLeft, wordWrap = false };
        _attributionStyle.fontSize = attributionFontSize;
        _attributionStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);

        string text = "Contains modified EUMETSAT data"
                    + "\nEarth imagery: NASA Blue Marble";

        float h = attributionFontSize * 2.6f;
        GUI.Label(new Rect(12f, Screen.height - h - 10f, 460f, h), text, _attributionStyle);
    }

    GUIStyle _attributionStyle;

    void TryBuild()
    {
        if (_ready) return;

        // PlanetController が作った簡易球を探して隠す
        var procedural = FindDeep(transform, "Globe");
        var grid = FindDeep(transform, "GlobeGrid");

        if (procedural == null && grid == null && _retryT < 0.5f) return;   // まだ作られていない

        if (matchPlanetController)
        {
            var pc = GetComponent("PlanetController");
            if (pc != null)
            {
                var f = pc.GetType().GetField("globeRadius");
                if (f != null) globeRadius = (float)f.GetValue(pc);
            }
        }

        // 地球を出さないときは、向こうが作った簡易球も緯線も含めて全部伏せる
        if (procedural != null) procedural.gameObject.SetActive(showEarth && !hideProceduralGlobe);
        if (grid != null) grid.gameObject.SetActive(showEarth && !hideGrid);

        if (showEarth)
        {
            BuildModel();
            if (useLiveClouds && cloudTexture == null) StartCoroutine(FetchLiveClouds());
        }

        if (setSpaceBackground)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = spaceColor;
            }
        }

        _ready = true;
    }

    void BuildModel()
    {
        var src = earthModel;
        if (src == null)
        {
#if UNITY_EDITOR
            src = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/EARTH.fbx");
#endif
        }
        if (src == null)
        {
            Debug.LogWarning("PlanetRealGlobe: EARTH モデルが見つかりません（Assets/Models/EARTH.fbx）");
            return;
        }

        var go = Instantiate(src, transform);
        go.name = "EarthModel";
        _model = go.transform;
        _model.localPosition = Vector3.zero;

        // Blender から付いてきたカメラと太陽は不要
        foreach (string junk in new string[] { "Camera", "Sun" })
        {
            var j = _model.Find(junk);
            if (j != null) Destroy(j.gameObject);
        }

        _surface = _model.Find("Surface");
        _clouds = _model.Find("Clouds");
        _atom = _model.Find("Atom");

        // モデルは半径 600 ほどで作られている。地表の実寸から縮尺を割り出す。
        float srcRadius = 1f;
        if (_surface != null)
        {
            var r = _surface.GetComponent<Renderer>();
            if (r != null) srcRadius = Mathf.Max(r.bounds.extents.x, r.bounds.extents.z);
        }
        if (srcRadius > 0.001f) _model.localScale = Vector3.one * (globeRadius / srcRadius);

        _model.localRotation = Quaternion.Euler(0f, 0f, axialTilt);

        // 低ポリの球はそのままだと面ごとに陰影が付き、三角形の切子が浮き出てしまう。
        // 球であることは分かっているので、法線は中心から外向きに取り直す。
        SmoothAsSphere(_surface);
        SmoothAsSphere(_clouds);
        SmoothAsSphere(_atom);

        // 三層はどれも同じ低ポリの多面体なので、間隔が狭いと面の中央で互いに潜り込み、
        // 地表が三角形に食い込んで見える。層の間を少し広げて交差を防ぐ。
        if (_clouds != null) _clouds.localScale *= cloudLift;
        if (_atom != null) _atom.localScale *= atmosphereLift;

        ApplyMaterials();
    }

    /// <summary>
    /// 球として法線を張り直す。頂点位置そのものが中心からの向きなので、
    /// それを正規化すれば、面の分割に依らないなめらかな陰影になる。
    /// </summary>
    static void SmoothAsSphere(Transform t)
    {
        if (t == null) return;
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        var mesh = Object.Instantiate(mf.sharedMesh);   // 元アセットは書き換えない
        var verts = mesh.vertices;
        var normals = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            normals[i] = verts[i].sqrMagnitude > 1e-12f ? verts[i].normalized : Vector3.up;
        mesh.normals = normals;
        mf.sharedMesh = mesh;
    }

    void ApplyMaterials()
    {
        if (_surface != null)
        {
            var tex = earthAlbedo != null ? earthAlbedo : GenerateEarthTexture();
            var mat = MakeMaterial(Color.white, transparent: false);
            SetTexture(mat, tex);
            SetSmoothness(mat, 0.10f);      // 海の照り返しが強すぎない程度
            _surface.GetComponent<Renderer>().sharedMaterial = mat;
        }

        if (_clouds != null)
        {
            var tex = cloudTexture != null ? cloudTexture : GenerateCloudTexture();
            var mat = MakeMaterial(new Color(1f, 1f, 1f, cloudOpacity), transparent: true);
            SetTexture(mat, tex);
            SetSmoothness(mat, 0.05f);
            _clouds.GetComponent<Renderer>().sharedMaterial = mat;
        }

        if (_atom != null)
        {
            // 大気は光を透かすだけの殻として扱う。陰影を付けると球がもう一枚あるように見えてしまう。
            var c = atmosphereColor; c.a = atmosphereOpacity;
            var mat = MakeMaterial(c, transparent: true, lit: false);
            _atom.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    /// <summary>
    /// いま地球にかかっている雲を取ってきて貼り替える。
    ///
    /// 手続きで作った雲は「雲らしい模様」でしかなく、どこにも実在しない。
    /// ここで貼るのは3時間前までの実際の観測なので、画面のなかの地球と、
    /// いま外に出れば見上げられる空が、同じものになる。
    ///
    /// 取得できるまでは生成した雲のままなので、通信が無くても破綻しない。
    /// 雲のデータは EUMETSAT による。使用時は "Contains modified EUMETSAT data" の表記が要る。
    /// </summary>
    System.Collections.IEnumerator FetchLiveClouds()
    {
        using (var req = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(liveCloudUrl, false))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("PlanetRealGlobe: 雲の取得に失敗（生成した雲のまま続けます）: " + req.error);
                yield break;
            }

            var tex = UnityEngine.Networking.DownloadHandlerTexture.GetContent(req);
            if (tex == null || _clouds == null) yield break;

            tex.wrapModeU = TextureWrapMode.Repeat;
            tex.wrapModeV = TextureWrapMode.Clamp;

            var mat = _clouds.GetComponent<Renderer>().sharedMaterial;
            SetTexture(mat, tex);
            Debug.Log("PlanetRealGlobe: 実際の雲を取得しました " + tex.width + "x" + tex.height);
        }
    }

    // ------------------------------------------------------------------
    // テクスチャ生成
    // ------------------------------------------------------------------

    /// <summary>
    /// 地表。標高だけで色を決めると、どこも同じ緑の星になってしまうので、
    /// 緯度（気温）と第二のノイズ（湿り）を掛け合わせて生態系を分ける。
    /// 赤道に森、その両側の乾いた帯に砂漠、高緯度に凍土と氷、という並びは
    /// 大気の循環がもたらすもので、地球の見た目をいちばん強く決めている。
    /// </summary>
    Texture2D GenerateEarthTexture()
    {
        int w = Mathf.Max(256, textureWidth);
        int h = w / 2;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;

        var rnd = new System.Random(seed);
        float o1 = (float)rnd.NextDouble() * 300f;
        float o2 = (float)rnd.NextDouble() * 300f + 500f;
        float o3 = (float)rnd.NextDouble() * 300f + 900f;

        var px = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float lat = (v - 0.5f) * 2f;          // -1(南極) .. 1(北極)
            float absLat = Mathf.Abs(lat);

            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;

                // 経度方向に継ぎ目が出ないよう、円筒座標をノイズ座標にする
                float ang = u * Mathf.PI * 2f;
                float nx = Mathf.Cos(ang) * 1.7f;
                float nz = Mathf.Sin(ang) * 1.7f;
                float ny = v * 3.4f;

                float elev = FBm(nx + o1, ny + o1, nz + o1, 6);
                elev = Mathf.Clamp01((elev - 0.5f) * 3.2f + 0.5f);
                elev -= absLat * 0.10f;                       // 極側はやや海に沈める

                float moisture = FBm(nx * 0.8f + o2, ny * 0.8f + o2, nz * 0.8f + o2, 4);
                moisture = Mathf.Clamp01((moisture - 0.5f) * 2.2f + 0.5f);

                Color c;

                if (elev < 0.50f)
                {
                    // 海。深さで色を変え、大陸棚をわずかに明るくする
                    float d = Mathf.InverseLerp(0.20f, 0.50f, elev);
                    c = Color.Lerp(oceanDeep, oceanShallow, d);
                    if (elev > 0.465f) c = Color.Lerp(c, shelf, Mathf.InverseLerp(0.465f, 0.50f, elev));
                }
                else
                {
                    float land = Mathf.InverseLerp(0.50f, 1f, elev);

                    // 気温（緯度）と湿りで生態系を選ぶ
                    float temp = 1f - absLat;
                    // 亜熱帯高圧帯（緯度25〜35度）は乾く
                    float dryBelt = Mathf.Exp(-Mathf.Pow((absLat - 0.32f) / 0.11f, 2f));
                    float dryness = Mathf.Clamp01((1f - moisture) * 0.55f + dryBelt * 0.45f);

                    if (temp > 0.62f)
                        c = Color.Lerp(forest, desert, SmoothBand(0.45f, 0.92f, dryness));
                    else if (temp > 0.35f)
                        c = Color.Lerp(grass, desert, SmoothBand(0.50f, 0.95f, dryness));
                    else
                        c = Color.Lerp(tundra, grass, moisture * 0.6f);

                    // 高いところは岩、さらに高ければ雪
                    if (land > 0.22f) c = Color.Lerp(c, rock, Mathf.InverseLerp(0.22f, 0.45f, land));
                    float snowLine = Mathf.Lerp(0.72f, 0.16f, absLat);
                    if (land > snowLine) c = Color.Lerp(c, ice, Mathf.InverseLerp(snowLine, snowLine + 0.18f, land));
                }

                // 極冠。海の上にも張り出す。
                float capNoise = FBm(nx * 2f + o3, ny * 2f + o3, nz * 2f + o3, 3);
                float capStart = 0.80f + (capNoise - 0.5f) * 0.10f;
                if (absLat > capStart) c = Color.Lerp(c, ice, Mathf.InverseLerp(capStart, capStart + 0.09f, absLat));

                px[y * w + x] = c;
            }
        }

        if (flipV) FlipVertically(px, w, h);
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>
    /// 雲。一様に散らすと霞んだ球になってしまう。
    /// 実際の雲は帯になっていて、赤道の収束帯と中緯度の低気圧帯に多く、
    /// そのあいだの亜熱帯高圧帯では晴れている。その粗密が惑星らしさを作る。
    /// </summary>
    Texture2D GenerateCloudTexture()
    {
        int w = Mathf.Max(256, textureWidth);
        int h = w / 2;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;

        var rnd = new System.Random(seed + 991);
        float o = (float)rnd.NextDouble() * 400f;

        // まず雲の濃さを素のまま作る
        var raw = new float[w * h];

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float lat = (v - 0.5f) * 2f;
            float absLat = Mathf.Abs(lat);

            // 緯度ごとの雲の出やすさ
            float itcz = Mathf.Exp(-Mathf.Pow(absLat / 0.10f, 2f));            // 赤道の収束帯
            float storm = Mathf.Exp(-Mathf.Pow((absLat - 0.62f) / 0.18f, 2f)); // 中緯度の低気圧帯
            float highs = Mathf.Exp(-Mathf.Pow((absLat - 0.30f) / 0.11f, 2f)); // 亜熱帯高圧帯（晴れ）
            float band = Mathf.Clamp01(0.55f + itcz * 0.45f + storm * 0.38f - highs * 0.40f);
            // 極はテクスチャが極端に引き伸ばされ、低ポリの面取りがそのまま出る。
            // 実際にも極上空は雲が少ないので、ここで薄めておく。
            band *= 1f - SmoothBand(0.82f, 0.97f, absLat);

            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float ang = u * Mathf.PI * 2f;
                float nx = Mathf.Cos(ang) * 2.6f;
                float nz = Mathf.Sin(ang) * 2.6f;
                float ny = v * 5.2f;

                // 多重ノイズの平均は 0.5 付近へ潰れる。伸ばさないと雲の濃淡が消える。
                float n = Mathf.Clamp01((FBm(nx + o, ny + o, nz + o, 6) - 0.5f) * 3.0f + 0.5f);
                // 東西に引き伸ばして、風に流された筋にする
                float streak = Mathf.Clamp01((FBm(nx * 0.35f + o + 77f, ny * 2.4f + o, nz * 0.35f + o, 3) - 0.5f) * 3.0f + 0.5f);
                raw[y * w + x] = (n * 0.72f + streak * 0.28f) * band;
            }
        }

        // 閾値は決め打ちにしない。
        // ノイズの取りうる値は係数をいじるたびに変わるので、固定の閾値だと
        // 「雲が全く出ない」「一面が雲」のどちらかに転びやすい。
        // 実際の分布を見て、指定した割合が雲になる高さで切る。
        float threshold = Quantile(raw, 1f - cloudCoverage);
        float softness = 0.10f * (Quantile(raw, 0.98f) - Quantile(raw, 0.02f) + 1e-5f);

        var px = new Color[w * h];
        for (int i = 0; i < raw.Length; i++)
        {
            float a = SmoothBand(threshold, threshold + softness, raw[i]);
            px[i] = new Color(1f, 1f, 1f, a);
        }

        if (flipV) FlipVertically(px, w, h);
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    /// <summary>値の分布のうえで、下から q の割合にあたる高さを返す。</summary>
    static float Quantile(float[] values, float q)
    {
        int n = Mathf.Min(4096, values.Length);
        var sample = new float[n];
        int stride = Mathf.Max(1, values.Length / n);
        for (int i = 0; i < n; i++) sample[i] = values[Mathf.Min(values.Length - 1, i * stride)];
        System.Array.Sort(sample);
        return sample[Mathf.Clamp(Mathf.RoundToInt(q * (n - 1)), 0, n - 1)];
    }

    static void FlipVertically(Color[] px, int w, int h)
    {
        for (int y = 0; y < h / 2; y++)
        {
            int a = y * w, b = (h - 1 - y) * w;
            for (int x = 0; x < w; x++)
            {
                var t = px[a + x]; px[a + x] = px[b + x]; px[b + x] = t;
            }
        }
    }

    /// <summary>
    /// GLSL の smoothstep にあたる、なめらかな階段。
    /// Unity の Mathf.SmoothStep(a, b, t) は a と b の「間を補間する」関数で、
    /// 閾値で切り分ける用途には使えない（返り値が a..b に収まってしまう）。
    /// </summary>
    static float SmoothBand(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-6f, edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    static float FBm(float x, float y, float z, int octaves)
    {
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

    // ------------------------------------------------------------------
    // ヘルパー
    // ------------------------------------------------------------------

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            var f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    Material MakeMaterial(Color color, bool transparent, bool lit = true)
    {
        Shader sh = lit
            ? (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            : (Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));

        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);

        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return m;
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
}
