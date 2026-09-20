using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// 旅路レポート。
///
/// 部屋の床に落ちた毛については、落ちたという結果しか残らない。どこを通り、
/// 何にぶつかり、どれだけ漂っていたかは、小さすぎて誰にも観測されない。
/// 星の規模に引き伸ばすと、その一本ぶんの出来事が測れる長さを持つ。
/// ここではそれを、景観学の調査記録の調子で淡々と控える。
///
/// 着地した順に一覧へ積み、選ぶとその一本の全文と、通ってきた軌跡が金色の線で出る。
/// 軌跡は、すべての毛が落ちきったときに星を包むワイヤーフレームの、一本ぶんにあたる。
/// </summary>
public class ProtoJourneyLog : MonoBehaviour
{
    // 再生中のスクリプト編集でドメインリロードが起きると静的フィールドだけが消えるので、
    // 見つからなければ探し直す。
    static ProtoJourneyLog _instance;
    public static ProtoJourneyLog Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<ProtoJourneyLog>();
            return _instance;
        }
        private set { _instance = value; }
    }

    public class Report
    {
        public int      index;
        public string   owner;
        public float    airTime, travelDistance, maxAltitude, peakSpeed;
        public int      caughtCount, contactCount;
        public float    lat, lon;
        public string   region;
        public string   landedClock;
        public List<(float t, string what)> events;
        public Vector3[] trail;
        public string   headline;      // 一覧に出す一行
        public ProtoHair hair;         // 地表に残っている本体。選ぶと光る
    }

    [Header("表示")]
    public int  fontSize = 12;
    public int  listRows = 16;

    [Header("記録の上限")]
    // 何十万本と積もる。全部の軌跡を保持するとメモリが持たないので、
    // 軌跡と出来事は先頭から一定数だけ持ち、それ以降は一行の要約だけ残す。
    // 金色のワイヤーフレームは、軌跡を持っているぶんで描く。
    [Tooltip("軌跡と出来事を保持するレポート数。これを超えた分は一行の要約のみ。")]
    public int maxDetailedReports = 30000;
    public Color trailColor = new Color(1f, 0.82f, 0.35f);
    public float trailWidth = 0.010f;

    readonly List<Report> _reports = new List<Report>();
    public IReadOnlyList<Report> Reports => _reports;
    static readonly List<(float t, string what)> _noEvents = new List<(float, string)>();

    int  _selected = -1;
    bool _panelOpen = true;
    Vector2 _scroll;

    LineRenderer _trailLine;
    Material _trailMat;
    Transform _globe;

    void Awake() { Instance = this; }
    void OnDestroy() { if (_instance == this) _instance = null; }

    void Start()
    {
        if (_globe != null) return;                       // 外から渡されていればそれを使う
        var g = GameObject.Find("Globe");                 // proto1 の自前シーン
        if (g == null)
        {
            var spin = FindAnyObjectByType<EarthSpin>();  // 既存シーンの自転する地球
            if (spin != null) g = spin.gameObject;
        }
        if (g != null) { _globe = g.transform; EnsureCalibrated(); }
    }

    public void Record(ProtoHair h)
    {
        var r = Build(h.ownerName, h.transform.position, h.airTime, h.travelDistance,
                      h.maxAltitude, h.peakSpeed, h.caughtCount, h.contactCount,
                      h.events, h.Trail, _globe);
        r.hair = h;
        h.reportIndex = _reports.Count - 1;
    }

    /// <summary>既存シーンに組み込んだ版（FieldHair）からの記録。球は呼び出し側が渡す。</summary>
    public void Record(FieldHair h, Transform globe)
    {
        if (_globe == null && globe != null) { _globe = globe; EnsureCalibrated(); }
        Build(h.ownerName, h.transform.position, h.airTime, h.travelDistance,
              h.maxAltitude, h.peakSpeed, h.caughtCount, h.contactCount,
              h.events, h.Trail, globe);
    }

    void EnsureCalibrated()
    {
        if (_globe == null) return;
        var mf = _globe.GetComponent<MeshFilter>();
        if (mf != null) ProtoGeo.Calibrate(mf.sharedMesh);
    }

    Report Build(string owner, Vector3 worldPos, float airTime, float travel,
                 float maxAlt, float peak, int caught, int contacts,
                 List<(float t, string what)> ev, IReadOnlyList<Vector3> trail, Transform globe)
    {
        // 着地点を球のローカルへ戻してから緯度経度を読む。星は自転している。
        float lat = 0f, lon = 0f;
        if (globe != null) ProtoGeo.LatLon(globe.InverseTransformPoint(worldPos), out lat, out lon);

        var r = new Report
        {
            index          = _reports.Count + 1,
            owner          = owner,
            airTime        = airTime,
            travelDistance = travel,
            maxAltitude    = maxAlt,
            peakSpeed      = peak,
            caughtCount    = caught,
            contactCount   = contacts,
            lat = lat, lon = lon,
            region         = ProtoGeo.Region(lat, lon),
            landedClock    = System.TimeSpan.FromSeconds(Time.timeSinceLevelLoad).ToString(@"hh\:mm\:ss"),
        };
        bool detailed = _reports.Count < maxDetailedReports;
        r.events = detailed ? new List<(float, string)>(ev) : _noEvents;
        r.trail  = detailed ? new List<Vector3>(trail).ToArray() : null;
        r.headline = $"#{r.index:D3}  {r.owner}  {r.airTime,5:F1}s  {r.region}";
        _reports.Add(r);
        return r;
    }

    /// <summary>組み込み先の星を外から教える。</summary>
    public void SetGlobe(Transform globe)
    {
        _globe = globe;
        EnsureCalibrated();
    }

    // ------------------------------------------------------------------
    // 軌跡
    // ------------------------------------------------------------------

    void ShowTrail(Report r)
    {
        if (r == null || r.trail == null || r.trail.Length < 2) { HideTrail(); return; }
        if (_trailMat == null)
        {
            _trailMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _trailMat.SetColor("_BaseColor", trailColor);
            _trailMat.SetFloat("_Surface", 1f);
            _trailMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            _trailMat.SetFloat("_DstBlend", (float)BlendMode.One);
            _trailMat.SetFloat("_ZWrite", 0f);
            _trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _trailMat.renderQueue = (int)RenderQueue.Transparent;
        }
        if (_trailLine == null)
        {
            // 軌跡は球のローカル座標で控えてあるので、線も星の子として置く。
            // そうすれば星が回っても、線の終点は着地した毛に付いたままになる。
            var go = new GameObject("JourneyTrail");
            go.transform.SetParent(_globe != null ? _globe : transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            _trailLine = go.AddComponent<LineRenderer>();
            _trailLine.material = _trailMat;
            _trailLine.useWorldSpace = false;
            _trailLine.numCapVertices = 4;
            _trailLine.shadowCastingMode = ShadowCastingMode.Off;
            _trailLine.receiveShadows = false;
        }
        // 球は直径ぶん拡大されているので、線の太さはその逆数で戻す
        float s = _globe != null ? Mathf.Max(_globe.lossyScale.x, 1e-4f) : 1f;
        _trailLine.widthMultiplier = trailWidth / s;
        _trailLine.positionCount = r.trail.Length;
        _trailLine.SetPositions(r.trail);
        _trailLine.enabled = true;
    }

    void HideTrail() { if (_trailLine != null) _trailLine.enabled = false; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.tabKey.wasPressedThisFrame)
        {
            _panelOpen = !_panelOpen;
            if (!_panelOpen) HideTrail();
        }
        // 上下キーで選択を移す
        if (_panelOpen && _reports.Count > 0 && kb != null)
        {
            if (kb.downArrowKey.wasPressedThisFrame) Select(Mathf.Min(_selected + 1, _reports.Count - 1));
            if (kb.upArrowKey.wasPressedThisFrame)   Select(Mathf.Max(_selected - 1, 0));
        }

        PickFromWorld();
    }

    /// <summary>
    /// 素の左クリックで、地表に落ちている毛そのものを選ぶ。
    /// 一覧の番号ではなく、目に留まった一本から記録へ辿れるほうが、観察の順序として自然。
    /// </summary>
    void PickFromWorld()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
        if (StaticLineField.ModifierHeld()) return;

        var cam = Camera.main;
        if (cam == null) return;
        Vector2 mp = mouse.position.ReadValue();

        // マウスはカメラのピクセル空間、OnGUI は Screen 空間で動く。
        // 高解像度ディスプレイやゲームビューの解像度指定で両者はずれるので、
        // パネルの当たり判定にはいちど揃えてから渡す。
        if (_panelOpen && cam.pixelWidth > 0 && cam.pixelHeight > 0)
        {
            float gx = mp.x / cam.pixelWidth  * Screen.width;
            float gy = mp.y / cam.pixelHeight * Screen.height;
            if (_lastPanelRect.Contains(new Vector2(gx, Screen.height - gy))) return;
        }

        Ray ray = cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
        {
            var h = hit.collider.GetComponentInParent<ProtoHair>();
            if (h != null && h.reportIndex >= 0)
            {
                Select(h.reportIndex);
                _panelOpen = true;
                _scrollToSelected = true;
                return;
            }
        }
        Select(-1);   // 何も無いところをクリックしたら解除
    }

    bool _scrollToSelected;
    Rect _lastPanelRect;
    Material _highlightMat;
    readonly List<Material[]> _savedMats = new List<Material[]>();
    ProtoHair _highlighted;

    void Select(int i)
    {
        _selected = i;
        var r = (i >= 0 && i < _reports.Count) ? _reports[i] : null;
        ShowTrail(r);
        Highlight(r != null ? r.hair : null);
    }

    /// <summary>選んだ一本だけ金色にする。どれを読んでいるのかが地表で分かるように。</summary>
    void Highlight(ProtoHair h)
    {
        if (_highlighted == h) return;

        if (_highlighted != null)
        {
            var rs = _highlighted.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rs.Length && i < _savedMats.Count; i++)
                if (rs[i] != null) rs[i].sharedMaterials = _savedMats[i];
            _savedMats.Clear();
        }

        _highlighted = h;
        if (h == null) return;

        if (_highlightMat == null)
        {
            _highlightMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _highlightMat.SetColor("_BaseColor", trailColor);
            _highlightMat.EnableKeyword("_EMISSION");
            _highlightMat.SetColor("_EmissionColor", trailColor * 2.2f);
        }
        foreach (var r in h.GetComponentsInChildren<Renderer>())
        {
            _savedMats.Add(r.sharedMaterials);
            var m = new Material[r.sharedMaterials.Length];
            for (int k = 0; k < m.Length; k++) m[k] = _highlightMat;
            r.sharedMaterials = m;
        }
    }

    // ------------------------------------------------------------------
    // 画面
    // ------------------------------------------------------------------

    GUIStyle _s, _sHead, _sDim, _sSel;

    void BuildStyles()
    {
        if (_s != null) return;
        _s = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = true, wordWrap = false };
        _s.normal.textColor = new Color(1f, 1f, 1f, 0.88f);
        _sHead = new GUIStyle(_s); _sHead.normal.textColor = new Color(1f, 0.85f, 0.45f, 1f);
        _sDim  = new GUIStyle(_s); _sDim.normal.textColor  = new Color(1f, 1f, 1f, 0.45f);
        _sSel  = new GUIStyle(_s); _sSel.normal.textColor   = new Color(0.2f, 0.15f, 0.05f, 1f);
    }

    /// <summary>終わりの場面では画面から引き上げる。読むための表示が、見る邪魔になる。</summary>
    public bool hidden;
    public void Hide() { hidden = true; _panelOpen = false; HideTrail(); Highlight(null); }

    void OnGUI()
    {
        if (hidden) return;
        BuildStyles();
        float lh = fontSize + 6f;

        // 操作の説明
        var field = StaticLineField.Instance;
        bool mod = StaticLineField.ModifierHeld();
        GUI.Label(new Rect(16, 10, 1100, lh),
            "右クリック(単押し): もやの点を置く   Enter/Esc: 引き終わる   C: もや消去   右ドラッグ: 視点   ホイール: ズーム", _sDim);
        GUI.Label(new Rect(16, 10 + lh, 1100, lh),
            "左クリック: 地表の毛を選ぶ   スペース: 頭を叩く   Tab: レポート", _sDim);

        // いま何本引いているか、修飾キーが届いているか。押しても表示が変わらなければ、
        // その環境では Cmd がエディタに吸われている。右クリック単押しで引ける。
        string state = field == null ? "" :
            (field.IsDrawing ? $"引いている途中: {field.NodeCount} 点" : "点を置くと引き始める")
            + $"   区間 {field.SegmentCount}"
            + (mod ? "   [Cmd/Ctrl 検出]" : "");
        GUI.Label(new Rect(16, 10 + lh * 2, 700, lh), state, mod ? _sHead : _sDim);

        if (!_panelOpen)
        {
            _lastPanelRect = new Rect(0, 0, 0, 0);
            GUI.Label(new Rect(16, 10 + lh * 3, 500, lh), $"着地 {_reports.Count} 本   Tab でレポートを開く", _sDim);
            return;
        }

        // ---- 一覧 ----
        float panelW = 300f, panelH = listRows * lh + 44f;
        float x = 16f, y = Screen.height - panelH - 16f;
        _lastPanelRect = new Rect(x, y, panelW + 18f + 460f, panelH);
        GUI.Box(new Rect(x, y, panelW, panelH), GUIContent.none);
        GUI.Label(new Rect(x + 10, y + 6, panelW - 20, lh), $"旅路レポート   全 {_reports.Count} 件", _sHead);

        var view = new Rect(x + 6, y + 6 + lh, panelW - 12, panelH - 16f - lh);
        var content = new Rect(0, 0, panelW - 32, Mathf.Max(_reports.Count, 1) * lh);
        // 地表の毛から選んだときは、一覧をその行まで送る
        if (_scrollToSelected && _selected >= 0)
        {
            _scroll.y = Mathf.Max(0f, _selected * lh - view.height * 0.5f);
            _scrollToSelected = false;
        }
        _scroll = GUI.BeginScrollView(view, _scroll, content);
        // 見えている行だけ描く。何十万件あっても、描くのは十数行。
        int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / lh) - 1);
        int last  = Mathf.Min(_reports.Count, first + Mathf.CeilToInt(view.height / lh) + 3);
        for (int i = first; i < last; i++)
        {
            var rect = new Rect(2, i * lh, panelW - 36, lh);
            bool sel = i == _selected;
            if (sel) GUI.Box(rect, GUIContent.none);
            if (GUI.Button(rect, _reports[i].headline, sel ? _sSel : _s)) Select(i);
        }
        GUI.EndScrollView();

        // ---- 全文 ----
        if (_selected < 0 || _selected >= _reports.Count)
        {
            GUI.Label(new Rect(x + panelW + 18, y + 6, 420, lh), "一覧から一本を選ぶ（↑↓キーでも移動）", _sDim);
            return;
        }

        var r = _reports[_selected];
        float dx = x + panelW + 18, dy = y + 6;
        float w = 460f;
        GUI.Box(new Rect(dx - 8, dy - 6, w, panelH), GUIContent.none);

        int line = 0;
        void L(string t, GUIStyle st = null) { GUI.Label(new Rect(dx, dy + line * lh, w - 16, lh), t, st ?? _s); line++; }

        L($"標本 #{r.index:D3}", _sHead);
        L($"由来        {r.owner}", _s);
        L($"着地時刻    {r.landedClock}", _s);
        L($"滞空時間    {r.airTime:F1} 秒", _s);
        L($"移動距離    {r.travelDistance:F2}", _s);
        L($"最高高度    {r.maxAltitude:F2}", _s);
        L($"最高速度    {r.peakSpeed:F2}", _s);
        L($"もやに捕獲  {r.caughtCount} 回", _s);
        L($"すれ違い    {r.contactCount} 本", _s);
        L($"着地座標    {ProtoGeo.Format(r.lat, r.lon)}", _s);
        L($"区域        {r.region}", _s);
        line++;
        L("経過", _sHead);
        int shown = 0;
        foreach (var e in r.events)
        {
            if (shown++ >= 10) { L("  …", _sDim); break; }
            L($"  {e.t,5:F1}s   {e.what}", _s);
        }
    }
}
