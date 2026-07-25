using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// スマートフォンの縦画面比率のなかで遊ぶ、抜け毛の一期一会。
///
/// ・人は常に画面の中央にいる。動くのは人ではなく、世界の側である。
/// ・床には抜け落ちた毛が落ちている。歩いているあいだにも、ときどき新たに抜ける。
/// ・画面の外に出た抜け毛は、そこで消える。戻ってももう無い。
///   一度離れた毛とは二度と会えない、という一点だけを規則にしている。
/// ・頭をクリックすると頭皮の画面へ。抜けてしばらく経っていれば、新しい毛が生えはじめている。
/// ・床の抜け毛をクリックするとその毛の画面へ。埃をまとっている。
///
/// 縦画面は Camera.rect で画面中央に 9:16 の枠を作り、外側は黒で塞いでいる。
/// 見えている範囲＝毛が消えるかどうかの境界なので、枠は演出ではなく規則そのものになっている。
///
/// 既存スクリプトには手を加えない独立ファイル。空の GameObject にアタッチして使う。
/// 毛・埃・頭皮はすべてコードで生成するため、追加のモデルは不要。
/// </summary>
public class MobileHairField : MonoBehaviour
{
    // ------------------------------------------------------------------
    // Inspector
    // ------------------------------------------------------------------

    [Header("画面（スマホ縦）")]
    [Tooltip("横 : 縦 の比。0.5625 = 9:16")]
    public float portraitAspect = 9f / 16f;
    public Color letterboxColor = Color.black;

    [Header("人と視点")]
    public Transform humanRoot;
    public Transform headTarget;
    [Tooltip("人は常に画面中央。動くのは世界の側なので、これは世界を送る速さでもある。")]
    public float moveSpeed = 2.4f;
    [Tooltip("人から見たカメラの位置")]
    public Vector3 cameraOffset = new Vector3(0f, 4.2f, -4.6f);
    [Tooltip("人のどの高さを画面中央に置くか")]
    public float lookHeight = 0.95f;
    public float clickRadius = 0.18f;

    [Header("床の抜け毛")]
    [Tooltip("最初から床に落ちている数")]
    public int initialHairs = 1;
    [Tooltip("最初の毛を撒く範囲（半径）")]
    public float scatterRadius = 3.2f;
    [Tooltip("歩いているとき、何秒ごとに一本抜けるか")]
    public float shedIntervalMin = 6f;
    public float shedIntervalMax = 13f;
    [Tooltip("画面外へ出た毛が消えるまでの時間（秒）")]
    public float vanishSeconds = 0.5f;
    [Tooltip("まだ出会っていない毛を、視界の外に何本くらい控えさせておくか（0で補充しない）")]
    public int unmetReserve = 0;
    public float replenishMinRadius = 7f;
    public float replenishMaxRadius = 16f;

    [Header("頭皮")]
    [Tooltip("最初に頭に生えている本数")]
    public int initialScalpHairs = 0;
    [Tooltip("抜けてから、次が生えはじめるまでの時間（秒）")]
    public float regrowDelay = 12f;
    [Tooltip("生えそろうまでの時間（秒）")]
    public float regrowSeconds = 25f;

    [Header("色")]
    public Color hairColor = new Color(0.11f, 0.10f, 0.09f);
    public Color scalpColor = new Color(0.93f, 0.80f, 0.70f);
    public Color dustColor = new Color(0.72f, 0.70f, 0.66f);
    public Color scalpViewBackground = new Color(0.94f, 0.93f, 0.91f);
    public Color strandViewBackground = new Color(0.90f, 0.89f, 0.87f);

    [Header("表示")]
    public bool showHud = true;
    public int hudFontSize = 20;

    // ------------------------------------------------------------------
    // 内部
    // ------------------------------------------------------------------

    enum View { Field, Scalp, Strand }

    class FloorHair
    {
        public Transform root;
        public int seed;
        public float vanishT = -1f;   // 画面外に出てからの経過。負なら画面内。
        public Vector3 baseScale = Vector3.one;
        public bool met;              // 一度でも画面に入ったか。出会う前の毛は、まだ失われない。
    }

    class ScalpHair
    {
        public Transform root;
        public float bornAt;          // 生えはじめた時刻
        public float seed;
    }

    View _view = View.Field;

    Camera _letterboxCam, _fieldCam, _scalpCam, _strandCam;
    Transform _scalpStage, _strandStage;
    Transform _scalpDome;
    FloorHair _focused;

    readonly List<FloorHair> _floor = new List<FloorHair>();
    readonly List<ScalpHair> _scalp = new List<ScalpHair>();

    float _nextShedAt;
    float _pendingRegrowAt = -1f;
    int _lostForever;

    Material _hairMat, _scalpMat, _dustMat;
    readonly RaycastHit[] _hits = new RaycastHit[32];
    GUIStyle _hudStyle;

    // ------------------------------------------------------------------

    void Start()
    {
        if (humanRoot == null)
        {
            var h = GameObject.Find("Human");
            if (h != null) humanRoot = h.transform;
        }
        if (headTarget == null && humanRoot != null) headTarget = humanRoot.Find("Head");
        if (headTarget != null && headTarget.GetComponent<Collider>() == null)
            headTarget.gameObject.AddComponent<SphereCollider>();

        _hairMat = MakeMaterial(hairColor);
        _scalpMat = MakeMaterial(scalpColor);
        _dustMat = MakeMaterial(dustColor);

        BuildCameras();
        BuildScalpStage();
        BuildStrandStage();

        ScatterInitialHairs();
        for (int i = 0; i < initialScalpHairs; i++)
            _scalp.Add(NewScalpHair(fullyGrown: true));

        // 頭に何も無く、床に毛が落ちている状態から始まる。
        // それは「たった今 抜けたあと」ということなので、次の一本はもう生えはじめようとしている。
        if (_scalp.Count == 0) _pendingRegrowAt = Time.time + regrowDelay;

        _nextShedAt = Time.time + Random.Range(shedIntervalMin, shedIntervalMax);
        SetView(View.Field);
    }

    void Update()
    {
        ApplyPortraitRect(_letterboxCam);
        ApplyPortraitRect(_fieldCam);
        ApplyPortraitRect(_scalpCam);
        ApplyPortraitRect(_strandCam);

        UpdateRegrow();

        switch (_view)
        {
            case View.Field: UpdateField(); break;
            case View.Scalp:
            case View.Strand:
                if (ClickedThisFrame() || EscapedThisFrame()) SetView(View.Field);
                break;
        }
    }

    // ------------------------------------------------------------------
    // 原っぱ（本編）
    // ------------------------------------------------------------------

    void UpdateField()
    {
        MoveHuman();
        FollowWithCamera();
        UpdateShedding();
        UpdateVanishing();
        ReplenishUnmet(unmetReserve, replenishMinRadius, replenishMaxRadius);
        HandleFieldClick();
    }

    void MoveHuman()
    {
        if (humanRoot == null) return;

        Vector2 axis = MoveAxis();
        if (axis.sqrMagnitude > 1f) axis.Normalize();

        Vector3 delta = new Vector3(axis.x, 0f, axis.y) * moveSpeed * Time.deltaTime;
        humanRoot.position += delta;

        // 進行方向へ体を向ける
        if (delta.sqrMagnitude > 1e-6f)
        {
            Quaternion want = Quaternion.LookRotation(new Vector3(delta.x, 0f, delta.z).normalized, Vector3.up);
            humanRoot.rotation = Quaternion.Slerp(humanRoot.rotation, want, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }
    }

    /// <summary>人は常に画面中央。補間を入れると中央からずれるので、毎フレーム厳密に合わせる。</summary>
    void FollowWithCamera()
    {
        if (_fieldCam == null || humanRoot == null) return;
        Vector3 aim = humanRoot.position + Vector3.up * lookHeight;
        _fieldCam.transform.position = humanRoot.position + cameraOffset;
        _fieldCam.transform.rotation = Quaternion.LookRotation((aim - _fieldCam.transform.position).normalized, Vector3.up);
    }

    /// <summary>
    /// 出会う前の毛を、視界の外に絶やさず置いておく。
    /// 別れがある以上、出会いも供給され続けなければ、歩く意味がなくなってしまう。
    /// </summary>
    void ReplenishUnmet(int want, float minR, float maxR)
    {
        if (want <= 0 || humanRoot == null) return;
        int unmet = 0;
        for (int i = 0; i < _floor.Count; i++) if (!_floor[i].met) unmet++;

        int spawn = Mathf.Min(want - unmet, 2);   // 一度に湧かせすぎない
        for (int i = 0; i < spawn; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(minR, maxR);
            Vector3 at = humanRoot.position + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            _floor.Add(SpawnFloorHair(at));
        }
    }

    void UpdateShedding()
    {
        // 頭にも床にも、毛は一本ずつ。
        // 床の毛が失われて初めて、頭の毛が抜ける。
        // 二本が同時に在ると「その一本」ではなくなり、別れが数の問題になってしまう。
        if (_floor.Count > 0) { _nextShedAt = Time.time + Random.Range(shedIntervalMin, shedIntervalMax); return; }

        if (Time.time < _nextShedAt) return;
        _nextShedAt = Time.time + Random.Range(shedIntervalMin, shedIntervalMax);

        if (_scalp.Count == 0) return;

        // 頭から一本失い、それが足もとに落ちる
        int idx = Random.Range(0, _scalp.Count);
        var lost = _scalp[idx];
        _scalp.RemoveAt(idx);
        if (lost.root != null) Destroy(lost.root.gameObject);

        Vector3 at = humanRoot != null ? humanRoot.position : Vector3.zero;
        at += new Vector3(Random.Range(-0.35f, 0.35f), 0f, Random.Range(-0.35f, 0.35f));
        _floor.Add(SpawnFloorHair(at));

        if (_pendingRegrowAt < 0f) _pendingRegrowAt = Time.time + regrowDelay;
    }

    /// <summary>
    /// 画面の外へ出た毛は、そこで消える。
    /// 見えなくなることと失われることを同じ出来事にしているので、
    /// 画面の縁がそのまま別れの線になる。
    /// </summary>
    void UpdateVanishing()
    {
        if (_fieldCam == null) return;

        for (int i = _floor.Count - 1; i >= 0; i--)
        {
            var h = _floor[i];
            if (h.root == null) { _floor.RemoveAt(i); continue; }

            Vector3 v = _fieldCam.WorldToViewportPoint(h.root.position);
            bool outside = v.z < 0f || v.x < -0.02f || v.x > 1.02f || v.y < -0.02f || v.y > 1.02f;

            if (!outside)
            {
                h.met = true;                                                // ここで出会った
                if (h.vanishT >= 0f) { h.vanishT = -1f; SetVanish(h, 1f); }  // 縁で引き返した分は助かる
                continue;
            }

            // まだ出会っていない毛は、画面の外で静かに待っている。
            // 出会う前に失われては、別れになりようがない。
            if (!h.met) continue;

            if (h.vanishT < 0f) h.vanishT = 0f;
            h.vanishT += Time.deltaTime;

            float a = 1f - Mathf.Clamp01(h.vanishT / Mathf.Max(0.01f, vanishSeconds));
            SetVanish(h, a);

            if (a <= 0f)
            {
                if (_focused == h) _focused = null;
                Destroy(h.root.gameObject);
                _floor.RemoveAt(i);
                _lostForever++;
            }
        }
    }

    void HandleFieldClick()
    {
        if (!ClickedThisFrame() || _fieldCam == null) return;

        Ray ray = _fieldCam.ScreenPointToRay(PointerPosition());
        int n = Physics.SphereCastNonAlloc(ray, clickRadius, _hits, Mathf.Infinity);

        FloorHair hitHair = null;
        bool hitHead = false;
        float bestHair = float.MaxValue;

        for (int i = 0; i < n; i++)
        {
            var col = _hits[i].collider;
            if (col == null) continue;

            if (headTarget != null && (col.transform == headTarget || col.transform.IsChildOf(headTarget)))
                hitHead = true;

            for (int j = 0; j < _floor.Count; j++)
            {
                var h = _floor[j];
                if (h.root == null) continue;
                if (col.transform == h.root || col.transform.IsChildOf(h.root))
                {
                    if (_hits[i].distance < bestHair) { bestHair = _hits[i].distance; hitHair = h; }
                }
            }
        }

        if (hitHair != null) { FocusStrand(hitHair); return; }
        if (hitHead) SetView(View.Scalp);
    }

    // ------------------------------------------------------------------
    // 生えなおし
    // ------------------------------------------------------------------

    void UpdateRegrow()
    {
        if (_pendingRegrowAt >= 0f && Time.time >= _pendingRegrowAt)
        {
            _scalp.Add(NewScalpHair(fullyGrown: false));
            _pendingRegrowAt = -1f;
        }

        // 伸びている途中の毛を伸ばす
        for (int i = 0; i < _scalp.Count; i++)
        {
            var s = _scalp[i];
            if (s.root == null) continue;
            float g = Mathf.Clamp01((Time.time - s.bornAt) / Mathf.Max(0.01f, regrowSeconds));
            ApplyScalpHairGrowth(s, g);
        }
    }

    ScalpHair NewScalpHair(bool fullyGrown)
    {
        var s = new ScalpHair();
        s.seed = Random.Range(0f, 1000f);
        s.bornAt = fullyGrown ? Time.time - regrowSeconds : Time.time;

        var root = new GameObject("ScalpHair").transform;
        root.SetParent(_scalpStage, false);

        // 頭皮ドームの上の、ばらけた位置に立てる
        float ang = Random.Range(0f, Mathf.PI * 2f);
        float r = Random.Range(0f, 1.05f);
        root.localPosition = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        root.localRotation = Quaternion.Euler(Random.Range(-9f, 9f), Random.Range(0f, 360f), Random.Range(-9f, 9f));
        s.root = root;

        BuildStrandGeometry(root, s.seed, withDust: false, thickness: 0.05f);
        ApplyScalpHairGrowth(s, fullyGrown ? 1f : 0f);
        return s;
    }

    void ApplyScalpHairGrowth(ScalpHair s, float g)
    {
        // 伸びるのは長さだけ。太さは変えない方が「生えかけ」に見える。
        float len = Mathf.Lerp(0.02f, 1f, g);
        s.root.localScale = new Vector3(1f, len, 1f);
    }

    // ------------------------------------------------------------------
    // 床の毛
    // ------------------------------------------------------------------

    void ScatterInitialHairs()
    {
        Vector3 center = humanRoot != null ? humanRoot.position : Vector3.zero;
        for (int i = 0; i < initialHairs; i++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(1.2f, scatterRadius);
            Vector3 at = center + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
            _floor.Add(SpawnFloorHair(at));
        }
    }

    FloorHair SpawnFloorHair(Vector3 at)
    {
        var h = new FloorHair();
        h.seed = Random.Range(0, 100000);

        var root = new GameObject("FloorHair").transform;
        root.SetParent(transform, false);
        root.position = at + Vector3.up * 0.02f;
        root.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);  // 寝かせる
        h.root = root;

        BuildStrandGeometry(root, h.seed, withDust: true, thickness: 0.045f);

        // クリックできるように、寝ている毛をひとまとめに覆う判定を置く
        var col = root.gameObject.AddComponent<CapsuleCollider>();
        col.direction = 1;      // Y（毛の伸びる向き）
        col.height = 1.25f;
        col.radius = 0.16f;
        col.center = new Vector3(0f, 0.45f, 0f);

        h.baseScale = root.localScale;
        return h;
    }

    /// <summary>
    /// 消えぎわの表現。半透明にするにはマテリアルを作り替える必要があり、
    /// 毛の本数だけ増えると重い。縮めて消す方が軽く、画面の縁で小さくなって
    /// いなくなる様子は「遠ざかって別れる」ようにも見える。
    /// </summary>
    void SetVanish(FloorHair h, float remain)
    {
        if (h.root == null) return;
        h.root.localScale = h.baseScale * Mathf.Clamp01(remain);
    }

    // ------------------------------------------------------------------
    // 毛の形（頭の毛も、抜けた毛も、同じ作りから生む）
    // ------------------------------------------------------------------

    void BuildStrandGeometry(Transform root, float seed, bool withDust, float thickness)
    {
        // ゆるく波打つ折れ線。抜けた毛は腰が抜けて、より曲がる。
        int segments = 5;
        float len = withDust ? 1.0f : 0.85f;
        float wave = withDust ? 0.16f : 0.06f;

        Vector3 prev = Vector3.zero;
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            float x = (Mathf.PerlinNoise(seed, t * 2.4f) - 0.5f) * 2f * wave;
            float z = (Mathf.PerlinNoise(seed + 37f, t * 2.4f) - 0.5f) * 2f * wave * 0.6f;
            Vector3 p = new Vector3(x, t * len, z);
            MakeSegment(root, "Seg" + i, prev, p, thickness * Mathf.Lerp(1f, 0.75f, t), _hairMat);
            if (i < segments) MakeJoint(root, "J" + i, p, thickness * Mathf.Lerp(1f, 0.75f, t), _hairMat);
            prev = p;
        }

        if (!withDust) return;

        // 埃。抜けてから床にある時間の長さが、そのまま身なりになっている。
        int dust = Random.Range(2, 5);
        for (int i = 0; i < dust; i++)
        {
            float t = Random.Range(0.15f, 0.95f);
            float x = (Mathf.PerlinNoise(seed, t * 2.4f) - 0.5f) * 2f * wave;
            float z = (Mathf.PerlinNoise(seed + 37f, t * 2.4f) - 0.5f) * 2f * wave * 0.6f;
            var speck = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            speck.name = "Dust";
            speck.transform.SetParent(root, false);
            speck.transform.localPosition = new Vector3(x, t * len, z)
                + Random.onUnitSphere * thickness * 0.9f;
            speck.transform.localScale = Vector3.one * Random.Range(thickness * 1.1f, thickness * 2.2f);
            speck.transform.localRotation = Random.rotation;
            StripCollider(speck);
            speck.GetComponent<MeshRenderer>().sharedMaterial = _dustMat;
        }
    }

    // ------------------------------------------------------------------
    // 画面の切り替え
    // ------------------------------------------------------------------

    void SetView(View v)
    {
        _view = v;
        if (_fieldCam != null) _fieldCam.enabled = (v == View.Field);
        if (_scalpCam != null) _scalpCam.enabled = (v == View.Scalp);
        if (_strandCam != null) _strandCam.enabled = (v == View.Strand);
        if (_scalpStage != null) _scalpStage.gameObject.SetActive(v == View.Scalp);
        if (_strandStage != null) _strandStage.gameObject.SetActive(v == View.Strand);
    }

    void FocusStrand(FloorHair h)
    {
        _focused = h;
        BuildFocusedStrand(h.seed);
        SetView(View.Strand);
    }

    // ------------------------------------------------------------------
    // 舞台づくり
    // ------------------------------------------------------------------

    void BuildCameras()
    {
        // 縦枠の外側を塞ぐためのカメラ。これが無いと枠外に前フレームの残像が出る。
        var lb = new GameObject("LetterboxCamera");
        lb.transform.SetParent(transform, false);
        _letterboxCam = lb.AddComponent<Camera>();
        _letterboxCam.clearFlags = CameraClearFlags.SolidColor;
        _letterboxCam.backgroundColor = letterboxColor;
        _letterboxCam.cullingMask = 0;          // 何も映さない。塗り潰すだけ。
        _letterboxCam.depth = -100;
        _letterboxCam.rect = new Rect(0f, 0f, 1f, 1f);

        // 本編のカメラは、シーンにある Main Camera をそのまま使う
        _fieldCam = Camera.main;
        if (_fieldCam == null)
        {
            var go = new GameObject("FieldCamera");
            go.transform.SetParent(transform, false);
            _fieldCam = go.AddComponent<Camera>();
        }
        _fieldCam.depth = 0;

        // 追従スクリプトが載っていると中央からずれるので止める
        var follow = _fieldCam.GetComponent("FollowCamera") as MonoBehaviour;
        if (follow != null) follow.enabled = false;
    }

    void BuildScalpStage()
    {
        var stage = new GameObject("ScalpStage").transform;
        stage.SetParent(transform, false);
        stage.position = new Vector3(0f, 1000f, 0f);
        _scalpStage = stage;

        var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dome.name = "Scalp";
        dome.transform.SetParent(stage, false);
        dome.transform.localScale = Vector3.one * 8f;
        dome.transform.localPosition = new Vector3(0f, -4f, 0f);
        StripCollider(dome);
        dome.GetComponent<MeshRenderer>().sharedMaterial = _scalpMat;
        _scalpDome = dome.transform;

        var camGo = new GameObject("ScalpCamera");
        camGo.transform.SetParent(stage, false);
        // 縦枠は横の視野が狭い。頭皮の広がりを見せるには、思うより引く必要がある。
        camGo.transform.localPosition = new Vector3(0f, 1.75f, -3.6f);
        camGo.transform.localRotation = Quaternion.LookRotation(
            (new Vector3(0f, 0.42f, 0f) - camGo.transform.localPosition).normalized, Vector3.up);
        _scalpCam = camGo.AddComponent<Camera>();
        _scalpCam.clearFlags = CameraClearFlags.SolidColor;
        _scalpCam.backgroundColor = scalpViewBackground;
        _scalpCam.depth = 1;

        AddStageLight(stage, new Vector3(35f, -25f, 0f));
    }

    void BuildStrandStage()
    {
        var stage = new GameObject("StrandStage").transform;
        stage.SetParent(transform, false);
        stage.position = new Vector3(200f, 1000f, 0f);
        _strandStage = stage;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floor.name = "Floor";
        floor.transform.SetParent(stage, false);
        floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        floor.transform.localScale = Vector3.one * 30f;
        StripCollider(floor);
        floor.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.80f, 0.78f, 0.75f));

        var camGo = new GameObject("StrandCamera");
        camGo.transform.SetParent(stage, false);
        // ほぼ真上から見下ろす。床に寝ている毛は、縦枠のなかで縦に伸びる方が収まりが良い。
        camGo.transform.localPosition = new Vector3(0f, 1.15f, -0.42f);
        camGo.transform.localRotation = Quaternion.LookRotation(
            (new Vector3(0f, 0.02f, 0.04f) - camGo.transform.localPosition).normalized, Vector3.up);
        _strandCam = camGo.AddComponent<Camera>();
        _strandCam.clearFlags = CameraClearFlags.SolidColor;
        _strandCam.backgroundColor = strandViewBackground;
        _strandCam.depth = 2;

        AddStageLight(stage, new Vector3(30f, -20f, 0f));
    }

    /// <summary>クリックされた毛と同じ姿を、拡大して組み直す。</summary>
    void BuildFocusedStrand(int seed)
    {
        var old = _strandStage.Find("Focused");
        if (old != null) Destroy(old.gameObject);

        var root = new GameObject("Focused").transform;
        root.SetParent(_strandStage, false);
        // 床に寝かせ、毛の伸びる向きを画面の縦に合わせる
        root.localPosition = new Vector3(0.04f, 0.02f, -0.42f);
        root.localRotation = Quaternion.Euler(90f, 6f, 0f);
        root.localScale = Vector3.one * 0.95f;

        Random.InitState(seed);
        BuildStrandGeometry(root, seed, withDust: true, thickness: 0.045f);
    }

    // ------------------------------------------------------------------
    // 縦画面
    // ------------------------------------------------------------------

    void ApplyPortraitRect(Camera cam)
    {
        if (cam == null) return;
        if (cam == _letterboxCam) { cam.rect = new Rect(0f, 0f, 1f, 1f); return; }

        float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        if (screenAspect > portraitAspect)
        {
            float w = portraitAspect / screenAspect;      // 左右を詰める（＝ふつうの横長画面）
            cam.rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        else
        {
            float h = screenAspect / portraitAspect;      // 上下を詰める
            cam.rect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }
    }

    // ------------------------------------------------------------------
    // 表示
    // ------------------------------------------------------------------

    void OnGUI()
    {
        if (!showHud) return;
        EnsureStyle();

        Rect r = _fieldCam != null ? _fieldCam.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
        float x = r.x + 16f;
        float top = Screen.height - r.yMax + 16f;

        string body;
        switch (_view)
        {
            case View.Scalp:
                body = "SCALP   " + (_scalp.Count > 0 ? "one hair" : "bare")
                     + (_pendingRegrowAt >= 0f ? "\nsomething is coming back" : "")
                     + "\n\nclick to go back";
                break;
            case View.Strand:
                body = "A SHED HAIR\n\nclick to go back";
                break;
            default:
                body = (_scalp.Count > 0 ? "one on the head" : "none on the head")
                     + "\n" + (_floor.Count > 0 ? "one on the floor" : "none on the floor")
                     + "\nparted with " + _lostForever
                     + "\n\nWASD / arrows to walk"
                     + "\nclick the head, or the hair on the floor";
                break;
        }
        GUI.Label(new Rect(x, top, r.width - 32f, 220f), body, _hudStyle);
    }

    void EnsureStyle()
    {
        if (_hudStyle == null)
            _hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true };
        _hudStyle.fontSize = hudFontSize;
        _hudStyle.normal.textColor = new Color(0.15f, 0.14f, 0.13f, 0.85f);
    }

    // ------------------------------------------------------------------
    // ヘルパー
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

    void AddStageLight(Transform parent, Vector3 euler)
    {
        var go = new GameObject("StageLight");
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(euler);
        var lt = go.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.1f;
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

    Vector2 MoveAxis()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;
        float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

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
