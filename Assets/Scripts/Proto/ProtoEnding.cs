using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// 終わり。
///
/// 星の表面が毛で覆い尽くされ、地面の色がもう見えなくなった瞬間を待つ。
/// それまで頭は生え、抜け、禿げて消え、また新しい頭が生まれて、毛を降らせ続ける。
///
/// 覆い尽くされたら、頭部の衛星はすべて薄れて消え、これまでに落ちた全ての毛の軌跡が
/// 金色の線として現れて星を包む。視点はプレイヤーの手に残る。星を回し、寄って、
/// 覆われた表面を眺められる。
///
/// しばらくして、Enter を促す。押すと、地表の毛が一本、静かに離れて宇宙へ飛び立つ。
/// 追っていくと、地球が小さくなり、その先に大きな惑星が現れる。地球もまた、
/// 何かの周りを回る衛星のひとつだったと分かる。
/// </summary>
[DefaultExecutionOrder(500)]
public class ProtoEnding : MonoBehaviour
{
    static ProtoEnding _instance;
    public static ProtoEnding Instance
    {
        get { if (_instance == null) _instance = FindAnyObjectByType<ProtoEnding>(); return _instance; }
    }

    enum Phase { Watching, Reveal, Prompt, Movie, Done }
    Phase _phase = Phase.Watching;
    float _phaseT;

    // ------------------------------------------------------------------
    [Header("終了条件：地表の被覆")]
    // 球面を等面積のセルに分け、セルに一定本数の毛が落ちたら「覆われた」とする。
    // 毛は細い線なので、一本ではその下の地面が透けて見える。何本か重なってはじめて隠れる。
    [Tooltip("球面を何個のセルに分けるか。多いほど細かく判定する。")]
    public int cellCount = 4000;
    // 覆うのは毛そのものでなければならない。細い毛が狂気的な量で集まって地面を隠す、
    // その一点がこの作品の価値で、面で塗って代用した瞬間に失われる。
    // だからここでは、実際に積もった毛の本数だけを数える。地面が隠れるまでには
    // 一区画あたり何十本も要る。それだけ落ちるまで、終わらない。
    [Tooltip("セルが覆われたと見なす毛の本数。地面が隠れるまで積もる量。")]
    public int hairsPerCell = 35;
    [Tooltip("この割合のセルが覆われたら終わる。1.0 は理論上ほぼ到達しないので少し下げる。")]
    [Range(0.5f, 1f)] public float coverageToEnd = 0.97f;
    [Tooltip("E キーで強制的に始める（確認用）。")]
    public bool debugKey = true;

    Vector3[] _cellDirs;         // 単位球上の方向（地球ローカル）
    int[]     _cellHits;
    int       _coveredCells;
    Transform _globe;
    Vector3   _center;
    float     _globeRadius = 1f, _orbitRadius = 1.25f;
    float     _checkTimer;
    // 終幕で飛び立たせる毛の候補（地球ローカル方向）。直近の着地から拾う。
    readonly List<Vector3> _recentLandings = new List<Vector3>();

    // ------------------------------------------------------------------
    [Header("顕現")]
    [Tooltip("頭部が消えるまでの秒数。")]
    public float headFadeSeconds = 3f;
    public Color goldColor = new Color(1f, 0.78f, 0.30f);
    [Range(0.01f, 1f)] public float goldAlpha = 0.18f;
    [Tooltip("この本数のときに goldAlpha そのまま。多いほど薄くする")]
    public float goldAlphaReferenceTrails = 2000f;
    [Tooltip("全部の軌跡が出そろうまでの秒数。")]
    public float weaveSeconds = 8f;
    [Tooltip("Enter の案内を出すまでの秒数。")]
    public float promptDelay = 10f;

    // ------------------------------------------------------------------
    [Header("終幕")]
    [Tooltip("毛が地表を離れて浮き上がる秒数。")]
    public float liftSeconds = 5f;
    [Tooltip("惑星へ向かって飛ぶ秒数。")]
    public float flightSeconds = 18f;
    [Tooltip("大きな惑星までの距離（地球半径の倍数）。")]
    public float planetDistance = 60f;
    [Tooltip("大きな惑星の半径（地球半径の倍数）。")]
    public float planetRadius = 14f;
    public Color planetColor = new Color(0.55f, 0.42f, 0.36f);
    [Tooltip("暗転にかける秒数。")]
    public float fadeOutSeconds = 3f;

    // ---- 顕現 ----
    readonly List<(Material m, Color baseCol)> _fadeMats = new List<(Material, Color)>();
    readonly List<GameObject> _heads = new List<GameObject>();
    Mesh _wireMesh; Material _wireMat; GameObject _wire;
    int[] _allIndices; int _lineSegments; int _lastShown = -1;

    // ---- 終幕 ----
    Camera _cam; MonoBehaviour _camRig;
    GameObject _flyingHair, _bigPlanet;
    Vector3 _liftDir;            // 飛び立つ方向（ワールド）
    Vector3 _hairStartWorld;
    float _blackAlpha;

    // ==================================================================

    void Start()
    {
        var pc = FindAnyObjectByType<PlanetController>();
        if (pc != null)
        {
            _center = pc.transform.position;
            _globeRadius = pc.globeRadius;
            _orbitRadius = pc.globeRadius + pc.orbitHeight;
            // 自動の生え戻しは止める。世代交代は PlanetRealHeads が担う。
            pc.regrowDelay = float.MaxValue;
        }
        var spin = FindAnyObjectByType<EarthSpin>();
        _globe = spin != null ? spin.transform : null;
        _cam = Camera.main;

        BuildCells();
    }

    /// <summary>球面に等面積で点を撒く（フィボナッチ球）。</summary>
    void BuildCells()
    {
        int n = Mathf.Max(64, cellCount);
        _cellDirs = new Vector3[n];
        _cellHits = new int[n];
        float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
        for (int i = 0; i < n; i++)
        {
            float y = 1f - (i + 0.5f) / n * 2f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float a = golden * i;
            _cellDirs[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }
    }

    /// <summary>半径 1 の細かい球。大きな惑星に使う。</summary>
    static Mesh BuildUVSphere(int lon, int lat)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
        for (int y = 0; y <= lat; y++)
        {
            float phi = Mathf.PI * y / lat;
            for (int x = 0; x <= lon; x++)
            {
                float th = 2f * Mathf.PI * x / lon;
                var p = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                v.Add(p); n.Add(p);
            }
        }
        for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x, b = a + lon + 1;
                tri.Add(a); tri.Add(a + 1); tri.Add(b);
                tri.Add(a + 1); tri.Add(b + 1); tri.Add(b);
            }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 };
        m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
        return m;
    }

    /// <summary>毛が地表に着いたとき、FieldHair から呼ばれる。</summary>
    public void RegisterLanding(Vector3 worldPos)
    {
        if (_phase != Phase.Watching || _cellDirs == null) return;
        Vector3 local = _globe != null ? _globe.InverseTransformPoint(worldPos) : worldPos - _center;
        Vector3 d = local.normalized;

        // 一番近いセル（総当たり。着地は毎秒数十本なので十分軽い）
        int best = 0; float bd = -2f;
        for (int i = 0; i < _cellDirs.Length; i++)
        {
            float dot = Vector3.Dot(_cellDirs[i], d);
            if (dot > bd) { bd = dot; best = i; }
        }
        _cellHits[best]++;
        if (_cellHits[best] == hairsPerCell) _coveredCells++;

        _recentLandings.Add(d);
        if (_recentLandings.Count > 64) _recentLandings.RemoveAt(0);
    }

    public float Coverage => _cellDirs == null ? 0f : (float)_coveredCells / _cellDirs.Length;

    // ==================================================================

    void Update()
    {
        _phaseT += Time.deltaTime;
        switch (_phase)
        {
            case Phase.Watching: TickWatching(); break;
            case Phase.Reveal:   TickReveal();   break;
            case Phase.Prompt:   TickReveal(); TickPrompt(); break;
            case Phase.Movie:    TickMovie();    break;
            case Phase.Done:     break;
        }
    }

    void TickWatching()
    {
        if (debugKey && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) { BeginReveal(); return; }
        _checkTimer -= Time.deltaTime;
        if (_checkTimer > 0f) return;
        _checkTimer = 0.5f;
        if (Coverage >= coverageToEnd) BeginReveal();
    }

    // ------------------------------------------------------------------
    // 顕現：頭が消え、金色の線が星を包む。視点は手元に残る。
    // ------------------------------------------------------------------

    public void BeginReveal()
    {
        if (_phase != Phase.Watching) return;
        _phase = Phase.Reveal; _phaseT = 0f;

        // 供給を止める。世代交代も止める。もやも置けなくなる。
        var pc = FindAnyObjectByType<PlanetController>(); if (pc != null) pc.enabled = false;
        var prh = FindAnyObjectByType<PlanetRealHeads>(); if (prh != null) prh.enabled = false;
        var ov = FindAnyObjectByType<ProtoFieldOverlay>(); if (ov != null) ov.enabled = false;
        var placer = FindAnyObjectByType<ProtoRightClickPlacer>(); if (placer != null) placer.enabled = false;
        var field = StaticLineField.Instance; if (field != null) field.enabled = false;
        var log = ProtoJourneyLog.Instance; if (log != null) log.Hide();

        PrepareHeadFade();
        BuildWireframe();
    }

    /// <summary>頭部の材質を透明にできる形へ複製して、薄れていけるようにする。</summary>
    void PrepareHeadFade()
    {
        _fadeMats.Clear(); _heads.Clear();
        foreach (var oh in FindObjectsByType<OrbitingHead>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            _heads.Add(oh.gameObject);
            foreach (var r in oh.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = new Material(mats[i]);
                    MakeTransparent(m);
                    Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                    _fadeMats.Add((m, c));
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }
    }

    static void MakeTransparent(Material m)
    {
        if (!m.HasProperty("_Surface")) return;
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
    }

    void TickReveal()
    {
        // 頭部が薄れて消える
        float f = Mathf.Clamp01(_phaseT / Mathf.Max(headFadeSeconds, 0.01f));
        float a = 1f - f;
        foreach (var (m, c) in _fadeMats)
            if (m != null && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, c.a * a));
        if (f >= 1f)
            foreach (var h in _heads) if (h != null && h.activeSelf) h.SetActive(false);

        // 金色の線が、落ちた順に星を包んでいく
        if (_wireMesh != null && _lineSegments > 0)
        {
            float w = Mathf.Clamp01((_phaseT - 1f) / Mathf.Max(weaveSeconds, 0.01f));
            SetVisibleSegments(Mathf.RoundToInt(w * _lineSegments));
        }

        if (_phase == Phase.Reveal && _phaseT >= promptDelay) { _phase = Phase.Prompt; }
    }

    void TickPrompt()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            BeginMovie();
    }

    // ------------------------------------------------------------------
    // 終幕：一本の毛が飛び立ち、大きな惑星へ。
    // ------------------------------------------------------------------

    void BeginMovie()
    {
        _phase = Phase.Movie; _phaseT = 0f;

        // 飛び立つ毛。着地したもののうち、いまカメラから見えている側のものを選ぶ。
        Vector3 bestDir = Vector3.up; float bestScore = -2f;
        Vector3 camDir = _cam != null ? (_cam.transform.position - _center).normalized : Vector3.back;
        foreach (var dLocal in _recentLandings)
        {
            Vector3 dWorld = _globe != null ? _globe.TransformDirection(dLocal) : dLocal;
            float s = Vector3.Dot(dWorld.normalized, camDir);
            if (s > bestScore) { bestScore = s; bestDir = dWorld.normalized; }
        }
        _liftDir = bestDir;
        _hairStartWorld = _center + _liftDir * (_globeRadius + 0.01f);

        // 一本の毛を作る（金色。星を包んだ線と同じ色）
        _flyingHair = new GameObject("DepartingHair");
        _flyingHair.transform.position = _hairStartWorld;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", goldColor);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", goldColor * 0.6f);   // Bloom が乗るので控えめに
        PlanetHair.BuildStrand(_flyingHair, _globeRadius * 0.09f, _globeRadius * 0.004f, mat, 0.9f);
        _flyingHair.transform.rotation = Quaternion.LookRotation(Vector3.Cross(_liftDir, Vector3.up).normalized, _liftDir);

        // 大きな惑星。毛が飛び立つ方向の先に置く。
        // 標準の球は面数が少なく、これだけ寄ると輪郭が多角形に見える。細かい球を作る。
        _bigPlanet = new GameObject("BigPlanet");
        _bigPlanet.transform.position = _center + _liftDir * (_globeRadius * planetDistance);
        _bigPlanet.transform.localScale = Vector3.one * (_globeRadius * planetRadius);
        _bigPlanet.AddComponent<MeshFilter>().sharedMesh = BuildUVSphere(96, 48);
        var pmr = _bigPlanet.AddComponent<MeshRenderer>();
        var pm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        pmr.sharedMaterial = pm;
        pm.SetColor("_BaseColor", planetColor);
        pm.SetFloat("_Smoothness", 0.12f);

        // ここからは視点を預かる
        if (_cam != null)
        {
            _camRig = _cam.GetComponent<PlanetCameraRig>();
            if (_camRig == null) _camRig = _cam.GetComponent<ProtoCameraRig>();
            if (_camRig != null) _camRig.enabled = false;
            var hover = _cam.GetComponent<HairHoverController>(); if (hover != null) hover.enabled = false;
        }
    }

    void TickMovie()
    {
        float t = _phaseT;
        Vector3 pos;
        float total = liftSeconds + flightSeconds;

        if (t < liftSeconds)
        {
            // 地表からゆっくり離れる。はじめは気づかないほど、次第にはっきりと。
            float u = t / liftSeconds;
            float h = _globeRadius * 0.35f * u * u;
            pos = _hairStartWorld + _liftDir * h;
        }
        else
        {
            // 惑星へ。加速しながら遠ざかる。
            float u = Mathf.Clamp01((t - liftSeconds) / flightSeconds);
            float e = u * u * u;
            float startH = _globeRadius * 0.35f;
            float endDist = _globeRadius * planetDistance - _globeRadius * planetRadius * 1.15f;   // 惑星の手前で止まる
            float dist = Mathf.Lerp(_globeRadius + startH, endDist, e);
            pos = _center + _liftDir * dist;
        }
        if (_flyingHair != null)
        {
            _flyingHair.transform.position = pos;
            _flyingHair.transform.Rotate(Vector3.forward, 20f * Time.deltaTime, Space.Self);
        }

        // カメラ。三つの段があり、見せたいものが変わる。
        //   浮上   … 毛のすぐ横、地表を背景に。離れていくのが見える。
        //   前半   … 毛の少し先で振り返って毛を見る。背後で地球が小さくなっていく。
        //   後半   … 毛の後ろへ回り込み、進む先を見る。大きな惑星が現れる。
        if (_cam != null)
        {
            Vector3 side = Vector3.Cross(_liftDir, Vector3.up).normalized;
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            Vector3 up = Vector3.Cross(side, _liftDir).normalized;

            Vector3 camPos; Vector3 lookAt = pos;
            if (t < liftSeconds)
            {
                float u = t / liftSeconds;
                camPos = pos + _liftDir * (_globeRadius * 0.10f) + side * (_globeRadius * 0.16f) + up * (_globeRadius * 0.05f);
                lookAt = pos - _liftDir * (_globeRadius * 0.02f * (1f - u));
            }
            else
            {
                float u = Mathf.Clamp01((t - liftSeconds) / flightSeconds);
                // 前半：先回りして振り返る。後半：後ろへ回り込んで前を見る。
                float swing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, u));
                float ahead  = Mathf.Lerp(_globeRadius * 0.35f, _globeRadius * 3.0f, u);
                float behind = _globeRadius * 4.0f;
                float along  = Mathf.Lerp(ahead, -behind, swing);
                // 振り返っている間は軸に近づけ、毛の真後ろに地球が来るようにする。
                // 回り込むときに横へ大きく開いて、惑星と毛を同じ画に入れる。
                float lateral = Mathf.Lerp(Mathf.Lerp(_globeRadius * 0.12f, _globeRadius * 0.5f, u), _globeRadius * 2.0f, swing);
                camPos = pos + _liftDir * along + side * lateral + up * lateral * 0.3f;
                Vector3 planetPos = _bigPlanet != null ? _bigPlanet.transform.position : pos;
                lookAt = Vector3.Lerp(pos, planetPos, swing * 0.6f);
            }
            // 地球の中に入らない。中に入ると裏側の面が抜けて、何を見ているのか分からなくなる。
            Vector3 fromC = camPos - _center;
            float minR = _globeRadius + 0.04f;
            if (fromC.magnitude < minR) camPos = _center + fromC.normalized * minR;

            float k = 1f - Mathf.Exp(-2.2f * Time.deltaTime);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, camPos, k);
            var want = Quaternion.LookRotation(lookAt - _cam.transform.position, Vector3.up);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, k);
        }

        // 終わり近くで暗転
        float fadeStart = total - fadeOutSeconds;
        _blackAlpha = t < fadeStart ? 0f : Mathf.Clamp01((t - fadeStart) / Mathf.Max(fadeOutSeconds, 0.01f));
        if (t >= total) { _phase = Phase.Done; _blackAlpha = 1f; }
    }

    // ------------------------------------------------------------------
    // 金色の線
    // ------------------------------------------------------------------

    void BuildWireframe()
    {
        var log = ProtoJourneyLog.Instance;
        if (log == null || log.Reports.Count == 0) return;

        var verts = new List<Vector3>(); var idx = new List<int>();
        int trails = 0;
        foreach (var r in log.Reports)
        {
            var tr = r.trail;
            if (tr == null || tr.Length < 2) continue;
            trails++;
            int b = verts.Count;
            for (int i = 0; i < tr.Length; i++) verts.Add(tr[i]);
            for (int i = 0; i + 1 < tr.Length; i++) { idx.Add(b + i); idx.Add(b + i + 1); }
        }
        if (idx.Count == 0) return;

        _wireMesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "JourneyWireframe" };
        _wireMesh.SetVertices(verts);
        _allIndices = idx.ToArray();
        _lineSegments = _allIndices.Length / 2;
        _wireMesh.SetIndices(System.Array.Empty<int>(), MeshTopology.Lines, 0);
        _wireMesh.bounds = new Bounds(Vector3.zero, Vector3.one * (_orbitRadius * 4f));

        // 線の本数で透明度を薄める。数千本で goldAlpha、数万本では重なりが増えるぶん薄くし、
        // 球が金の塊に潰れずに、糸の束として見えるようにする。
        float density = Mathf.Clamp01(goldAlphaReferenceTrails / Mathf.Max(trails, 1));
        float alpha = Mathf.Max(goldAlpha * Mathf.Pow(density, 0.8f), 0.01f);

        _wireMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        _wireMat.SetColor("_BaseColor", new Color(goldColor.r, goldColor.g, goldColor.b, alpha));
        _wireMat.SetFloat("_Surface", 1f);
        _wireMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        _wireMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        _wireMat.SetFloat("_ZWrite", 0f);
        _wireMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _wireMat.renderQueue = (int)RenderQueue.Transparent;

        _wire = new GameObject("JourneyWireframe");
        _wire.transform.SetParent(_globe != null ? _globe : transform, false);
        _wire.transform.localPosition = Vector3.zero;
        _wire.transform.localRotation = Quaternion.identity;
        _wire.transform.localScale = Vector3.one;
        _wire.AddComponent<MeshFilter>().sharedMesh = _wireMesh;
        var mr = _wire.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _wireMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void SetVisibleSegments(int n)
    {
        if (_wireMesh == null) return;
        n = Mathf.Clamp(n, 0, _lineSegments);
        if (n == _lastShown) return;
        _lastShown = n;
        if (n == 0) { _wireMesh.SetIndices(System.Array.Empty<int>(), MeshTopology.Lines, 0); return; }
        // 配列を切り出さず、先頭 n*2 個だけを渡す（毎フレームの確保を避ける）
        _wireMesh.SetIndices(_allIndices, 0, n * 2, MeshTopology.Lines, 0, false);
    }

    // ------------------------------------------------------------------
    // 画面
    // ------------------------------------------------------------------

    GUIStyle _sBig, _sSmall;
    void OnGUI()
    {
        if (_sBig == null)
        {
            _sBig = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            _sSmall = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperRight };
        }

        if (_phase == Phase.Watching)
        {
            _sSmall.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            GUI.Label(new Rect(Screen.width - 316, 12, 300, 22),
                $"地表の被覆  {Coverage * 100f:F1}%   ({_coveredCells}/{_cellDirs?.Length ?? 0} 区画)", _sSmall);
            return;
        }

        if (_phase == Phase.Reveal || _phase == Phase.Prompt)
        {
            _sBig.normal.textColor = new Color(1f, 0.85f, 0.5f, Mathf.Clamp01(_phaseT / 2f) * 0.9f);
            GUI.Label(new Rect(0, Screen.height * 0.08f, Screen.width, 36), "毛の惑星", _sBig);
        }
        if (_phase == Phase.Prompt)
        {
            float blink = 0.55f + 0.45f * Mathf.Sin(Time.time * 2.2f);
            _sBig.normal.textColor = new Color(1f, 1f, 1f, blink);
            GUI.Label(new Rect(0, Screen.height * 0.5f - 18, Screen.width, 36), "Enter を押してください", _sBig);
        }

        if (_blackAlpha > 0f)
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, _blackAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
            if (_phase == Phase.Done)
            {
                _sBig.normal.textColor = new Color(1f, 0.85f, 0.5f, 0.9f);
                GUI.Label(new Rect(0, Screen.height * 0.5f - 18, Screen.width, 36), "終", _sBig);
            }
        }
    }
}
