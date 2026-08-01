using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// planetReal の頭を、球＋放射状の毛から、頭らしい形と髪型へ作り直す。
///
/// ■ 頭が球に見えてしまう理由
/// 人の頭は球ではない。横に狭く前後に長く、後頭部が張り出し、顎に向かって細くなる。
/// このうちどれが欠けても「頭」ではなく「球に顔が描いてあるもの」に見える。
/// ここでは球の頂点を、その四つの特徴に沿って動かしている。UV は動かさないので、
/// 既に貼られている顔テクスチャはそのまま乗る。
///
/// ■ 毛が刺さって見える理由
/// 毛が頭皮の法線方向へまっすぐ立っていると、生えているというより刺さって見える。
/// 実際の髪は、頭頂のつむじから放射状に「寝て」流れ、頭の丸みに沿う。
/// そこで各毛を、法線ではなく つむじから遠ざかる接線方向へ向け直し、
/// わずかに浮かせて厚みを出している。生え際も、前は高く後ろは低いという形に整えた。
///
/// PlanetController（共同作業者の実装）には一切手を触れない。
/// 向こうが作った頭と毛を、後から形と向きだけ直している。
/// </summary>
[DefaultExecutionOrder(200)]   // PlanetController が頭を作り終えたあとに動く
public class PlanetRealHeads : MonoBehaviour
{
    [Header("頭のモデル")]
    [Tooltip("実際の頭のモデル（未設定なら球を頭の形へ変形して使う）")]
    public GameObject headModel;
    [Tooltip("モデルを使うとき、頭の高さをこの倍率で合わせる")]
    public float modelScale = 1.0f;
    [Tooltip("モデルの向き調整（度）。顔が +Z を向くように。")]
    public float modelYaw = 0f;

    [Header("眼鏡")]
    [Tooltip("スキャンから切り出した眼鏡フレーム。未設定なら Assets/Models/monden_glasses.obj を読み込む。")]
    public GameObject glassesModel;
    [Tooltip("眼鏡をかける")]
    public bool showGlasses = true;
    public Color glassesColor = new Color(0.05f, 0.05f, 0.06f);
    [Tooltip("フレームの艶。プラスチックの黒は完全なつや消しではない。")]
    [Range(0f, 1f)] public float glassesSmoothness = 0.62f;

    [Header("肌")]
    [Tooltip("未設定なら生成する")]
    public Texture2D skinTexture;
    [Tooltip("間引きで失われた凹凸と、毛穴の肌理。未設定なら Assets/Textures/monden_face_normal.png")]
    public Texture2D skinNormalMap;
    [Tooltip("小鼻の脇・目のくぼみ・唇の合わせ目に入る影。未設定なら monden_face_ao.png")]
    public Texture2D skinOcclusionMap;
    [Tooltip("皮脂の照りの分布（A にスムースネス）。未設定なら monden_face_mask.png")]
    public Texture2D skinMaskMap;
    [Range(0f, 3f)] public float normalStrength = 1.0f;
    [Range(0f, 1f)] public float occlusionStrength = 0.85f;
    public Color skinBase = new Color(0.86f, 0.68f, 0.57f);
    public Color skinShadow = new Color(0.62f, 0.44f, 0.36f);
    public Color skinFlush = new Color(0.84f, 0.55f, 0.47f);
    public int skinTextureWidth = 1024;

    [Header("頭のかたち")]
    [Tooltip("横幅（1で球のまま）。人の頭は横に狭い。")]
    [Range(0.6f, 1.2f)] public float width = 0.86f;
    [Tooltip("後頭部の張り出し")]
    [Range(0f, 0.3f)] public float occiput = 0.17f;
    [Tooltip("顎へ向けた絞り込み")]
    [Range(0f, 0.6f)] public float jawTaper = 0.30f;
    [Tooltip("額の平坦さ")]
    [Range(0f, 0.2f)] public float foreheadFlatten = 0.05f;
    [Tooltip("頭頂の平坦さ")]
    [Range(0f, 0.2f)] public float crownFlatten = 0.06f;

    [Header("髪型")]
    [Tooltip("つむじの位置（頭の中心から見た向き）")]
    public Vector3 crownDirection = new Vector3(0.12f, 0.95f, -0.28f);
    [Tooltip("生え際の高さ。顔側ではこれだけ高くなる。")]
    [Range(0f, 1f)] public float hairlineFront = 0.42f;
    [Tooltip("生え際の高さ。後ろ側の下限。")]
    [Range(-0.5f, 0.6f)] public float hairlineBack = 0.02f;
    [Tooltip("毛をどれだけ寝かせるか。0で頭皮に沿って完全に寝る。")]
    [Range(0f, 1f)] public float lift = 0.14f;
    [Tooltip("毛の流れのばらつき")]
    [Range(0f, 0.6f)] public float flowJitter = 0.16f;
    [Tooltip("頭皮からわずかに浮かせる量（毛が埋まらないように）")]
    public float scalpOffset = 0.008f;
    [Tooltip("毛が顔へ流れるのをどれだけ避けるか。1でほぼ真横・後ろへ回す。")]
    [Range(0f, 1.5f)] public float faceAvoidance = 1.1f;

    [Header("動作")]
    [Tooltip("生え際より下の毛を隠す（生え際を作るために間引く）")]
    public bool hideBelowHairline = true;

    [Header("頭を据えて見る")]
    [Tooltip("頭をひとつだけ原点に据え、周回も抜け毛も止める。造形と髪型を詰めるとき用。")]
    public bool focusMode = false;
    [Tooltip("据えた頭をゆっくり回して、全方向を確かめられるようにする（度/秒）")]
    public float focusSpinDeg = 12f;
    [Tooltip("カメラを頭に寄せる")]
    public bool focusCamera = true;
    [Tooltip("画面の高さに対して頭を何倍の余裕で収めるか。1.0でぴったり、大きいほど引く。")]
    public float focusDistance = 1.6f;

    [Header("毛のボリューム（①：頭皮の毛を1メッシュで増量）")]
    [Tooltip("頭皮に沿って寝かせた細い毛を多数生成し、1メッシュに焼き込む。円柱は描画停止し抜け毛の器として残す。")]
    public bool volumeHair = true;
    [Tooltip("有毛領域を覆う黒い薄殻（キャップ）。これで毛の隙間から肌が透けず、髪に見える。")]
    public bool hairCap = true;
    [Tooltip("キャップを頭皮からどれだけ浮かせるか")]
    public float capOffset = 0.003f;
    [Tooltip("抜けきったときに生え際がどれだけ後退するか（0で不変、大きいほどハゲ上がる）")]
    [Range(0f, 1f)] public float capRecede = 0.5f;
    [Tooltip("キャップ上に生やす毛の本数（見た目の毛流れ・質感）。多いほど重い。")]
    public int volumeStrandCount = 260;
    [Tooltip("毛の長さ。キャップ上の毛流れとして短めが自然。")]
    public float volumeStrandLength = 0.12f;
    [Tooltip("毛1本の太さ（細いほど自然）")]
    public float volumeThickness = 0.0035f;
    [Tooltip("毛の断面の面数（3＝三角柱で軽い）")]
    [Range(2, 6)] public int volumeSides = 3;
    [Tooltip("毛の折れ点の数（多いほど滑らかに曲がる＝重い）")]
    [Range(2, 8)] public int volumeSegments = 4;
    [Tooltip("毛を頭皮からどれだけ浮かせるか。0で頭皮に完全に沿う（＝寝かせる）。")]
    [Range(0f, 1f)] public float volumeLift = 0.08f;

    bool _ready;
    float _retryT;

    void Start() { TryApply(); }

    void Update()
    {
        if (_ready) return;
        _retryT += Time.deltaTime;
        if (_retryT < 3f) TryApply();
    }

    void TryApply()
    {
        int heads = 0;
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("OrbitingHead")) continue;
            ApplyToHead(child);
            heads++;
        }

        if (heads > 0)
        {
            if (focusMode) EnterFocusMode();
            _ready = true;
            Debug.Log("PlanetRealHeads: " + heads + " 個の頭を整えました" + (focusMode ? "（1つを据えて表示）" : ""));
        }
    }

    /// <summary>
    /// 頭をひとつだけ残して原点に据える。
    /// 周回していると形を確かめられないし、時間が経つほど毛が抜けて髪型が変わってしまうので、
    /// PlanetController ごと止めてしまう（生成はもう終わっている）。
    /// </summary>
    void EnterFocusMode()
    {
        var pc = GetComponent("PlanetController") as MonoBehaviour;
        if (pc != null) pc.enabled = false;

        Transform kept = null;
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("OrbitingHead")) continue;
            if (kept == null)
            {
                kept = child;
                var orbit = child.GetComponent("OrbitingHead") as MonoBehaviour;
                if (orbit != null) orbit.enabled = false;
                child.position = transform.position;
                child.rotation = Quaternion.identity;
            }
            else child.gameObject.SetActive(false);
        }
        _focused = kept;

        if (focusCamera && kept != null)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var rig = cam.GetComponent("PlanetCameraRig") as MonoBehaviour;
                if (rig != null) rig.enabled = false;

                // 頭の実寸から画角を決める。決め打ちの距離だと、モデルを差し替えるたびに枠から外れる。
                Bounds b = new Bounds(kept.position, Vector3.zero);
                bool first = true;
                foreach (var r in kept.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (first) { b = r.bounds; first = false; }
                    else b.Encapsulate(r.bounds);
                }

                float size = Mathf.Max(b.size.x, b.size.y);
                float dist = size * focusDistance / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));

                // 顔は +Z を向いている。そちら側から、少し斜め上に構える。
                cam.transform.position = b.center + new Vector3(-0.30f, 0.16f, 1f).normalized * dist;
                cam.transform.LookAt(b.center);
                _focusCenter = b.center;
            }
        }
    }

    Transform _focused;
    Vector3 _focusCenter;

    void LateUpdate()
    {
        // 据えた頭をゆっくり回す。正面だけ見て詰めると、横と後ろが破綻する。
        if (focusMode && _focused != null && focusSpinDeg != 0f)
            _focused.RotateAround(_focusCenter, Vector3.up, focusSpinDeg * Time.deltaTime);

        TickVolume();
    }

    void ApplyToHead(Transform head)
    {
        var skull = head.Find("Skull");
        if (skull == null) return;

        Vector3 skullScale = skull.localScale;
        _radiusTable = null;

        if (headModel != null)
        {
            SwapInModel(head, skull);
        }
        else
        {
            ReshapeSkull(skull);
        }

        Vector3 crown = crownDirection.normalized;
        // つむじの向きは頭ごとに少しずらす。全員が同じ分け目だと、群れとして嘘になる。
        int seed = head.name.GetHashCode();
        var rnd = new System.Random(seed);
        crown = (crown + new Vector3(
            ((float)rnd.NextDouble() - 0.5f) * 0.25f,
            0f,
            ((float)rnd.NextDouble() - 0.5f) * 0.25f)).normalized;

        foreach (Transform strand in head)
        {
            if (!strand.name.StartsWith("ScalpHair")) continue;
            StyleStrand(strand, crown, skullScale, rnd);
        }

        if (volumeHair && head.Find("HairVolume") == null)
            BuildVolumeHair(head, crown, skullScale, rnd);
    }

    // ==================================================================
    // 毛のボリューム（①）
    // 頭皮の毛を、多数の毛束を1つのメッシュへ焼き込んで増量する。
    // 向きは StyleStrand と同じ「つむじからの流れ・生え際・頭皮に沿う曲がり」で決める。
    // 抜けた頭皮毛の数に応じて表示本数を減らし、ハゲていく様子を保つ。
    // PlanetController の円柱の毛は描画だけ止め、抜け毛カウントの器として残す。
    // ==================================================================

    class VolumeHead
    {
        public Transform head;
        public MeshFilter mf;
        public List<Vector3[]> strands;   // 頭皮に沿わせた毛の芯線（頭ローカル）
        public int refCount = 1;          // 満タン時の頭皮毛数（残り割合の基準）
        public float lastBoost = -1f;     // いま適用中の生え際後退量（変化検知用）

        // 有毛領域を覆う薄殻（キャップ）のグリッド。生え際後退で縁を削って薄くする。
        public Vector3[] capPos;
        public Vector3[] capNrm;
        public float[]   capMargin;       // その頂点の (d.y - 生え際)。boost より大きい所だけ張る。
        public int capAZ, capRows;
    }
    readonly List<VolumeHead> _volumeHeads = new List<VolumeHead>();
    float _volumeTick;

    static readonly List<Vector3> _mv = new List<Vector3>();
    static readonly List<Vector3> _mn = new List<Vector3>();
    static readonly List<int>     _mt = new List<int>();

    void BuildVolumeHair(Transform head, Vector3 crown, Vector3 skullScale, System.Random rnd)
    {
        var orbit = head.GetComponent<OrbitingHead>();
        Material mat = orbit != null ? orbit.hairMat : null;

        // 頭皮に沿って寝かせた毛を独立生成してキャッシュする（SurfaceRadius が要るのでこの場で作る）
        var strands = new List<Vector3[]>(volumeStrandCount);
        int guard = volumeStrandCount * 25;
        while (strands.Count < volumeStrandCount && guard-- > 0)
        {
            var poly = ComputeVolumeStrand(RandomScalpDir(rnd), crown, skullScale, rnd);
            if (poly != null) strands.Add(poly);
        }

        var go = new GameObject("HairVolume");
        go.transform.SetParent(head, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        var vh = new VolumeHead
        {
            head = head,
            mf = go.GetComponent<MeshFilter>(),
            strands = strands,
            refCount = Mathf.Max(1, CountActiveScalpStrands(head)),
        };
        if (hairCap) BuildCapGrid(vh, skullScale);   // SurfaceRadius が要るのでこの場で作る
        _volumeHeads.Add(vh);

        HideScalpCylinders(head);
        RebuildVolumeMesh(vh, 0f);
    }

    // 有毛領域を覆う薄殻グリッドを、頭皮に沿ってこの場で焼き込む（SurfaceRadius が正しい間に）。
    void BuildCapGrid(VolumeHead vh, Vector3 skullScale)
    {
        int AZ = 72, EL = 28;
        int cols = AZ + 1;          // 継ぎ目の列を複製して三角形を張りやすくする
        int rows = EL + 1;
        vh.capAZ = AZ; vh.capRows = rows;
        vh.capPos = new Vector3[cols * rows];
        vh.capNrm = new Vector3[cols * rows];
        vh.capMargin = new float[cols * rows];

        for (int e = 0; e < rows; e++)
        {
            float y = Mathf.Lerp(0.999f, -0.25f, (float)e / EL);   // 頭頂→生え際下まで
            float rxz = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            for (int a = 0; a < cols; a++)
            {
                float az = (float)a / AZ * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(az) * rxz, y, Mathf.Sin(az) * rxz);
                int idx = e * cols + a;
                vh.capPos[idx] = d * (SurfaceRadius(d, skullScale) + capOffset);
                vh.capNrm[idx] = d;
                float hl = Mathf.Lerp(hairlineBack, hairlineFront, Mathf.Clamp01(d.z * 0.5f + 0.5f));
                vh.capMargin[idx] = d.y - hl;   // >0 で有毛
            }
        }
    }

    // 頭皮側へ偏らせた根本方向をひとつ返す
    Vector3 RandomScalpDir(System.Random rnd)
    {
        float az  = (float)(rnd.NextDouble() * 2.0 * Mathf.PI);
        float y   = Mathf.Lerp(-0.1f, 1f, (float)rnd.NextDouble());
        float rxz = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(Mathf.Cos(az) * rxz, y, Mathf.Sin(az) * rxz);
    }

    // 根本方向 d0 から、頭皮に沿って流れて寝る毛の芯線を返す（生え際で切る。生えないなら null）。
    // StyleStrand と同じ考え方（つむじからの流れ・頭皮に沿う弧・生え際）。SurfaceRadius が正しく働く前提。
    Vector3[] ComputeVolumeStrand(Vector3 d0, Vector3 crown, Vector3 skullScale, System.Random rnd)
    {
        d0 = d0.normalized;

        float hairline = Mathf.Lerp(hairlineBack, hairlineFront, Mathf.Clamp01(d0.z * 0.5f + 0.5f));
        if (d0.y < hairline) return null;

        Vector3 flow = d0 * Vector3.Dot(crown, d0) - crown;
        if (flow.sqrMagnitude < 1e-6f) flow = Vector3.ProjectOnPlane(new Vector3(0f, 0f, -1f), d0);
        flow.Normalize();

        float towardFace = Vector3.Dot(flow, Vector3.forward);
        if (towardFace > 0f) flow = (flow - Vector3.forward * towardFace * faceAvoidance).normalized;

        flow = Vector3.ProjectOnPlane(flow + new Vector3(
            ((float)rnd.NextDouble() - 0.5f) * flowJitter,
            ((float)rnd.NextDouble() - 0.5f) * flowJitter,
            ((float)rnd.NextDouble() - 0.5f) * flowJitter), d0).normalized;

        Vector3 axis = Vector3.Cross(d0, flow).normalized;
        if (axis.sqrMagnitude < 1e-6f) return null;

        float r0 = SurfaceRadius(d0, skullScale);
        if (r0 < 1e-4f) return null;
        // 毛の長さ分だけ頭皮上を回る角度。長いほど深く回り込む。寝かせる（volumeLift 小）ほど素直に沿う。
        float totalAngle = (volumeStrandLength / r0) * Mathf.Rad2Deg * Mathf.Lerp(1f, 0.55f, volumeLift);

        // 生え際を越えたら切る
        int probe = 24;
        for (int i = 1; i <= probe; i++)
        {
            float a = totalAngle * i / probe;
            Vector3 d = Quaternion.AngleAxis(a, axis) * d0;
            float limit = Mathf.Lerp(hairlineBack, hairlineFront, Mathf.Clamp01(d.z * 0.5f + 0.5f));
            if (d.y < limit) { totalAngle = totalAngle * (i - 1) / probe; break; }
        }
        if (totalAngle < 3f) return null;

        int n = Mathf.Max(2, volumeSegments);
        var pts = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            Vector3 d = Quaternion.AngleAxis(totalAngle * t, axis) * d0;
            float h = scalpOffset + volumeLift * r0 * t * t;   // 毛先ほどわずかに浮かせて厚みを出す
            pts[i] = d * (SurfaceRadius(d, skullScale) + h);
        }
        return pts;
    }

    // 生え際後退量 boost に応じて、キャップと毛を描いてメッシュを焼き直す（boost 大＝ハゲ上がる）
    void RebuildVolumeMesh(VolumeHead vh, float boost)
    {
        vh.lastBoost = boost;

        _mv.Clear(); _mn.Clear(); _mt.Clear();
        int sides = Mathf.Clamp(volumeSides, 2, 6);

        if (hairCap && vh.capPos != null) AppendCap(vh, boost);

        // 生え際（＋後退量）より上の毛だけ描く
        for (int s = 0; s < vh.strands.Count; s++)
        {
            var st = vh.strands[s];
            Vector3 dn = st[0].normalized;
            float hl = Mathf.Lerp(hairlineBack, hairlineFront, Mathf.Clamp01(dn.z * 0.5f + 0.5f));
            if (dn.y - hl < boost) continue;
            AppendTube(st, volumeThickness, sides);
        }

        var mesh = vh.mf.sharedMesh;
        if (mesh == null || mesh.name != "HairVolumeMesh")
        {
            mesh = new Mesh { name = "HairVolumeMesh" };
            vh.mf.sharedMesh = mesh;
        }
        mesh.Clear();
        if (_mv.Count > 0)
        {
            mesh.indexFormat = _mv.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_mv);
            mesh.SetNormals(_mn);
            mesh.SetTriangles(_mt, 0);
            mesh.RecalculateBounds();
        }
    }

    // 芯線 pts に沿った細い管を _mv/_mn/_mt へ追加する
    void AppendTube(Vector3[] pts, float thickness, int sides)
    {
        int P = pts.Length;
        if (P < 2) return;
        int baseV = _mv.Count;

        Vector3 tan0 = (pts[1] - pts[0]).normalized;
        Vector3 up = Mathf.Abs(tan0.y) > 0.9f ? Vector3.right : Vector3.up;
        Vector3 nrm = Vector3.Cross(tan0, up).normalized;
        Vector3 bin = Vector3.Cross(tan0, nrm).normalized;

        for (int i = 0; i < P; i++)
        {
            float th = thickness * Mathf.Lerp(1f, 0.3f, (float)i / (P - 1)); // 先細り
            for (int s = 0; s < sides; s++)
            {
                float ang = (float)s / sides * Mathf.PI * 2f;
                Vector3 off = (nrm * Mathf.Cos(ang) + bin * Mathf.Sin(ang)) * th;
                _mv.Add(pts[i] + off);
                _mn.Add(off.sqrMagnitude > 1e-12f ? off.normalized : nrm);
            }
        }

        for (int i = 0; i < P - 1; i++)
        {
            int r0i = baseV + i * sides;
            int r1i = baseV + (i + 1) * sides;
            for (int s = 0; s < sides; s++)
            {
                int a = r0i + s, b = r0i + (s + 1) % sides;
                int c = r1i + s, d = r1i + (s + 1) % sides;
                _mt.Add(a); _mt.Add(c); _mt.Add(b);
                _mt.Add(b); _mt.Add(c); _mt.Add(d);
            }
        }
    }

    // キャップ（有毛領域の薄殻）を _mv/_mn/_mt へ追加する。boost より上（有毛）の升だけ張る。
    void AppendCap(VolumeHead vh, float boost)
    {
        int AZ = vh.capAZ, rows = vh.capRows, cols = AZ + 1;
        int baseV = _mv.Count;
        for (int i = 0; i < vh.capPos.Length; i++) { _mv.Add(vh.capPos[i]); _mn.Add(vh.capNrm[i]); }

        for (int e = 0; e < rows - 1; e++)
            for (int a = 0; a < AZ; a++)
            {
                int i00 = e * cols + a, i01 = e * cols + a + 1;
                int i10 = (e + 1) * cols + a, i11 = (e + 1) * cols + a + 1;
                if (vh.capMargin[i00] > boost && vh.capMargin[i01] > boost &&
                    vh.capMargin[i10] > boost && vh.capMargin[i11] > boost)
                {
                    _mt.Add(baseV + i00); _mt.Add(baseV + i10); _mt.Add(baseV + i01);
                    _mt.Add(baseV + i01); _mt.Add(baseV + i10); _mt.Add(baseV + i11);
                }
            }
    }

    void TickVolume()
    {
        if (_volumeHeads.Count == 0) return;
        _volumeTick += Time.deltaTime;
        if (_volumeTick < 0.25f) return;
        _volumeTick = 0f;

        for (int i = _volumeHeads.Count - 1; i >= 0; i--)
        {
            var vh = _volumeHeads[i];
            if (vh.head == null) { _volumeHeads.RemoveAt(i); continue; }

            HideScalpCylinders(vh.head);   // 再生した円柱の毛も隠す

            // 残っている頭皮毛の割合に応じて生え際を後退させる＝抜けるほどハゲ上がる
            int live = CountActiveScalpStrands(vh.head);
            float frac = Mathf.Clamp01((float)live / vh.refCount);
            float boost = (1f - frac) * capRecede;
            if (Mathf.Abs(boost - vh.lastBoost) >= 0.02f) RebuildVolumeMesh(vh, boost);
        }
    }

    int CountActiveScalpStrands(Transform head)
    {
        int c = 0;
        foreach (Transform strand in head)
        {
            if (!strand.name.StartsWith("ScalpHair")) continue;
            if (!strand.gameObject.activeSelf) continue;
            if (strand.childCount == 0) continue;
            c++;
        }
        return c;
    }

    void HideScalpCylinders(Transform head)
    {
        foreach (Transform strand in head)
        {
            if (!strand.name.StartsWith("ScalpHair")) continue;
            var rs = strand.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) if (rs[i].enabled) rs[i].enabled = false;
        }
    }

    // ------------------------------------------------------------------
    // 頭のかたち
    // ------------------------------------------------------------------

    void ReshapeSkull(Transform skull)
    {
        var mf = skull.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        if (mf.sharedMesh.name.StartsWith("HeadShape")) return;   // 二重適用を避ける

        var mesh = Instantiate(mf.sharedMesh);    // 元のプリミティブは書き換えない
        mesh.name = "HeadShape";

        var verts = mesh.vertices;
        var normals = new Vector3[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            float r = v.magnitude;
            if (r < 1e-6f) continue;
            Vector3 d = v / r;                    // 単位球上の向き
            verts[i] = Deform(d) * r;
        }

        mesh.vertices = verts;

        // 変形後の面から法線を取り直す（球の法線のままだと陰影が形に合わない）
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;
    }

    /// <summary>
    /// 単位球の一点を、頭のかたちへ動かす。
    /// +Z が顔の向き、+Y が頭頂。
    /// </summary>
    Vector3 Deform(Vector3 d)
    {
        Vector3 p = d;

        // 横に狭く
        p.x *= width;

        // 顎に向けた絞り込み。下へ行くほど細く、わずかに前へ出る。
        float lower = Mathf.Clamp01(Mathf.InverseLerp(-0.10f, -1f, d.y));
        float taper = 1f - jawTaper * lower * lower;
        p.x *= taper;
        p.z *= taper;
        p.z += lower * lower * 0.10f;

        // 後頭部の張り出し
        float occBand = Mathf.Exp(-Mathf.Pow((d.y - 0.05f) / 0.55f, 2f));
        p.z -= occiput * occBand * Mathf.Clamp01(-d.z);

        // 額はやや平ら
        float foreBand = Mathf.Exp(-Mathf.Pow((d.y - 0.45f) / 0.32f, 2f));
        p.z -= foreheadFlatten * foreBand * Mathf.Clamp01(d.z);

        // 頭頂もわずかに平ら
        p.y -= crownFlatten * Mathf.Clamp01(d.y) * Mathf.Pow(Mathf.Clamp01(d.y), 3f);

        return p;
    }

    // ------------------------------------------------------------------
    // 髪型
    // ------------------------------------------------------------------

    void StyleStrand(Transform strand, Vector3 crown, Vector3 skullScale, System.Random rnd)
    {
        // もとの向き（＝生えている位置）を、根本の位置から取り直す
        Vector3 d0 = strand.localPosition.sqrMagnitude > 1e-8f
            ? strand.localPosition.normalized
            : Vector3.up;

        // 生え際。顔側は高く、後ろは低い。
        float hairline = Mathf.Lerp(hairlineBack, hairlineFront, Mathf.Clamp01(d0.z * 0.5f + 0.5f));
        if (d0.y < hairline)
        {
            if (hideBelowHairline) strand.gameObject.SetActive(false);
            return;
        }
        strand.gameObject.SetActive(true);

        // つむじから遠ざかる接線方向。これが髪の流れになる。
        Vector3 flow = d0 * Vector3.Dot(crown, d0) - crown;
        if (flow.sqrMagnitude < 1e-6f)
            flow = Vector3.ProjectOnPlane(new Vector3(0f, 0f, -1f), d0);
        flow.Normalize();

        // 顔の方へ流れると、毛が顔を横切って面を隠してしまう。
        // 実際の髪も、前に落ちるのは生え際のわずかな範囲だけで、大半は後ろと横へ流れる。
        float towardFace = Vector3.Dot(flow, Vector3.forward);
        if (towardFace > 0f) flow = (flow - Vector3.forward * towardFace * faceAvoidance).normalized;

        // ばらつき。全部が同じ向きだと鬘に見える。
        flow = Vector3.ProjectOnPlane(flow + new Vector3(
            ((float)rnd.NextDouble() - 0.5f) * flowJitter,
            ((float)rnd.NextDouble() - 0.5f) * flowJitter,
            ((float)rnd.NextDouble() - 0.5f) * flowJitter), d0).normalized;

        // 毛の全長は、既にある円柱の並びから測る（長さの決定は向こうの実装に任せる）
        var segs = new System.Collections.Generic.List<Transform>();
        foreach (Transform c in strand) segs.Add(c);
        if (segs.Count == 0) return;

        float length = 0f;
        float thickness = 0.01f;
        for (int i = 0; i < segs.Count; i++)
        {
            length += segs[i].localScale.y * 2f;      // 円柱は高さ2なので scale.y は半分の長さ
            thickness = segs[i].localScale.x;
        }

        // 根本を頭の原点に据え、子の円柱を頭のローカル座標へ直接並べる
        strand.localPosition = Vector3.zero;
        strand.localRotation = Quaternion.identity;
        strand.localScale = Vector3.one;

        // 頭皮に沿う円弧を作る。
        // 直線のまま寝かせると、球の接線はすぐ表面から離れてしまい、
        // 毛が頭から浮いた棒に見える。実際の髪は頭の丸みに沿って曲がる。
        Vector3 axis = Vector3.Cross(d0, flow).normalized;
        float r0 = SurfaceRadius(d0, skullScale);
        float totalAngle = (length / Mathf.Max(1e-4f, r0)) * Mathf.Rad2Deg * Mathf.Lerp(1f, 0.55f, lift);

        // 生え際を越えたところで切る。
        // 頭皮に沿って伸ばすだけだと、額から先へ回り込んで顔を横切ってしまう。
        // 実際の髪は生え際で終わっていて、その線があるから顔が顔として見える。
        int probe = 24;
        for (int i = 1; i <= probe; i++)
        {
            float a = totalAngle * i / probe;
            Vector3 d = Quaternion.AngleAxis(a, axis) * d0;
            float limit = Mathf.Lerp(hairlineBack, hairlineFront, Mathf.Clamp01(d.z * 0.5f + 0.5f));
            if (d.y < limit) { totalAngle = totalAngle * (i - 1) / probe; break; }
        }
        if (totalAngle < 1f) { strand.gameObject.SetActive(false); return; }

        int n = segs.Count;
        var pts = new Vector3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n;
            Vector3 d = Quaternion.AngleAxis(totalAngle * t, axis) * d0;
            // 先へ行くほどわずかに浮かせ、毛束としての厚みを出す
            float h = scalpOffset + lift * r0 * t * t;
            pts[i] = d * (SurfaceRadius(d, skullScale) + h);
        }

        for (int i = 0; i < n; i++)
        {
            Vector3 a = pts[i], b = pts[i + 1];
            Vector3 v = b - a;
            float len = v.magnitude;
            if (len < 1e-6f) continue;

            var seg = segs[i];
            seg.localPosition = (a + b) * 0.5f;
            seg.localRotation = Quaternion.FromToRotation(Vector3.up, v / len);
            seg.localScale = new Vector3(thickness, len * 0.5f, thickness);
        }
    }

    /// <summary>その向きにおける頭皮までの距離。モデルがあれば実際の面から測る。</summary>
    float SurfaceRadius(Vector3 dir, Vector3 skullScale)
    {
        if (_radiusTable != null) return SampleRadiusTable(dir.normalized);
        Vector3 shaped = Deform(dir.normalized);
        return Vector3.Scale(shaped, skullScale * 0.5f).magnitude;
    }

    // ------------------------------------------------------------------
    // 実際の頭モデルに差し替える
    // ------------------------------------------------------------------

    void SwapInModel(Transform head, Transform skull)
    {
        // 元の球は消さずに描画だけ止める。向こうの実装が Skull を参照していても壊れないように。
        var sr = skull.GetComponent<Renderer>();
        if (sr != null) sr.enabled = false;

        if (head.Find("HeadModel") != null) return;   // 二重適用を避ける

        var go = Instantiate(headModel, head);
        go.name = "HeadModel";
        go.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);

        // モデルは最大寸法が1に正規化してある。頭の高さを元の球に合わせる。
        float targetHeight = skull.localScale.y * modelScale;
        go.transform.localScale = Vector3.one * targetHeight;

        // このモデルは胸像で、原点が肩まで含めた中心にある。
        // そのままだと毛を生やす基準がずれるので、頭蓋の中心を原点へ持ってくる。
        go.transform.localPosition = Vector3.zero;
        Vector3 cranium = CraniumCenter(go.transform);
        go.transform.localPosition = -cranium;

        // 頭のメッシュが二つのサブメッシュ（skin / frame）を持つ場合、
        // フレームは肌とは別のマテリアルで塗れる。眼鏡を重ねる必要はない。
        var mat = MakeSkinMaterial();
        bool frameInMesh = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            int sub = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.subMeshCount : 1;
            if (sub >= 2)
            {
                var mats = new Material[sub];
                mats[0] = mat;
                for (int i = 1; i < sub; i++) mats[i] = MakeGlassesMaterial();
                r.sharedMaterials = mats;
                frameInMesh = true;
            }
            else r.sharedMaterial = mat;
        }

        // 毛を生やす基準（頭皮までの距離）は、眼鏡を付ける前に測る。
        // 付けたあとだと、フレームの上から毛が生えてしまう。
        BuildRadiusTable(go.transform);

        if (showGlasses && !frameInMesh) AttachGlasses(go.transform);
    }

    /// <summary>
    /// 眼鏡フレームを、頭とは別のメッシュとしてかぶせる。
    ///
    /// テクスチャに黒く描くやり方は、この頭では成立しない。
    /// UV が球面投影——原点から見た向きで貼り付ける方式——なので、
    /// フレームと、そのすぐ裏にある肌が、まったく同じ座標を共有してしまう。
    /// フレームを黒く塗ると裏の肌も黒くなり、少しでも塗りを広げれば
    /// フレームの外へはみ出して、目のまわりの肌に黒が乗る。
    /// 平面に描いた絵で立体を表そうとする限り、この滲みは避けられない。
    ///
    /// なので、絵ではなく物として置く。スキャンの高解像度データから
    /// フレームの面だけを切り出したメッシュ（monden_glasses.obj）を、
    /// 黒いマテリアルで頭に重ねる。頭のメッシュは13,000頂点に間引いた分だけ
    /// 凹凸がなだらかになっているので、フレームはわずかに外へ出して沈み込みを防ぐ。
    /// </summary>
    void AttachGlasses(Transform model)
    {
        if (model.Find("Glasses") != null) return;

        var src = glassesModel;
        if (src == null)
        {
#if UNITY_EDITOR
            src = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/monden_glasses.obj");
#endif
        }
        if (src == null)
        {
            Debug.LogWarning("PlanetRealHeads: 眼鏡モデルが見つかりません（Assets/Models/monden_glasses.obj）");
            return;
        }

        // 頭モデルと同じ正規化空間で書き出してあるので、そのまま子にすれば重なる。
        var g = Instantiate(src, model);
        g.name = "Glasses";
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;
        g.transform.localScale = Vector3.one;

        var m = MakeGlassesMaterial();
        foreach (var r in g.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
    }

    /// <summary>黒いプラスチックのフレーム。別メッシュにも、サブメッシュにも同じものを使う。</summary>
    Material MakeGlassesMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", glassesColor);
        if (m.HasProperty("_Color")) m.SetColor("_Color", glassesColor);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", glassesSmoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.0f);
        return m;
    }

    /// <summary>
    /// 頭蓋のあたりの中心。上から 45% ぶんの頂点の重心を使う。
    /// 胸像の重心を使うと首や肩に引っ張られ、毛が顔を横切ってしまう。
    /// </summary>
    Vector3 CraniumCenter(Transform model)
    {
        float top = float.MinValue, bottom = float.MaxValue;
        var pts = new System.Collections.Generic.List<Vector3>();

        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            foreach (var v in mf.sharedMesh.vertices)
            {
                Vector3 p = model.localRotation * Vector3.Scale(v, model.localScale);
                pts.Add(p);
                if (p.y > top) top = p.y;
                if (p.y < bottom) bottom = p.y;
            }
        }
        if (pts.Count == 0) return Vector3.zero;

        float cut = top - (top - bottom) * 0.45f;
        Vector3 sum = Vector3.zero; int cnt = 0;
        foreach (var p in pts) if (p.y >= cut) { sum += p; cnt++; }
        return cnt > 0 ? sum / cnt : Vector3.zero;
    }

    /// <summary>
    /// 向きごとの頭皮までの距離を、実際の頂点から表にしておく。
    /// 毛を生やすたびにメッシュ全体を探すのは重いので、方位・仰角の格子に最大半径を溜める。
    /// </summary>
    void BuildRadiusTable(Transform model)
    {
        const int AZ = 64, EL = 32;
        var table = new float[AZ * EL];

        foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var verts = mf.sharedMesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                // 頭のローカル空間（＝毛を置く空間）へ移す
                Vector3 p = model.localPosition + model.localRotation * Vector3.Scale(verts[i], model.localScale);
                float r = p.magnitude;
                if (r < 1e-6f) continue;
                Vector3 d = p / r;
                int idx = DirIndex(d, AZ, EL);
                if (r > table[idx]) table[idx] = r;
            }
        }

        // 空いた升は、周りから埋める（スキャンの穴や粗い所で 0 にならないように）
        for (int pass = 0; pass < 3; pass++)
        {
            for (int e = 0; e < EL; e++)
                for (int a = 0; a < AZ; a++)
                {
                    int i = e * AZ + a;
                    if (table[i] > 0f) continue;
                    float sum = 0f; int cnt = 0;
                    for (int de = -1; de <= 1; de++)
                        for (int da = -1; da <= 1; da++)
                        {
                            int ee = Mathf.Clamp(e + de, 0, EL - 1);
                            int aa = ((a + da) % AZ + AZ) % AZ;
                            float v = table[ee * AZ + aa];
                            if (v > 0f) { sum += v; cnt++; }
                        }
                    if (cnt > 0) table[i] = sum / cnt;
                }
        }

        _radiusTable = table;
        _tableAz = AZ; _tableEl = EL;
    }

    static int DirIndex(Vector3 d, int AZ, int EL)
    {
        float az = Mathf.Atan2(d.x, d.z) / (2f * Mathf.PI) + 0.5f;
        float el = Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) / Mathf.PI + 0.5f;
        int a = Mathf.Clamp(Mathf.FloorToInt(az * AZ), 0, AZ - 1);
        int e = Mathf.Clamp(Mathf.FloorToInt(el * EL), 0, EL - 1);
        return e * AZ + a;
    }

    float SampleRadiusTable(Vector3 d)
    {
        return _radiusTable[DirIndex(d, _tableAz, _tableEl)];
    }

    float[] _radiusTable;
    int _tableAz, _tableEl;

    // ------------------------------------------------------------------
    // 肌
    // ------------------------------------------------------------------

    /// <summary>
    /// 肌のマテリアル。
    ///
    /// 色を貼っただけの顔が作り物に見えるのは、肌が光に対して一様に振る舞ってしまうからだ。
    /// 実際の顔は、細かい凹凸で光を散らし（法線）、くぼみで光を失い（遮蔽）、
    /// 皮脂の乗った所だけ強く照り返す（粗さの分布）。この三つが揃って初めて、
    /// 光の当たり方が「肌のそれ」になる。色より先に、この三つを渡す。
    /// </summary>
    Material MakeSkinMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh);

        var tex = skinTexture != null ? skinTexture : LoadTex("monden_face") ?? GenerateSkinTexture();
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);

        var nrm = skinNormalMap != null ? skinNormalMap : LoadTex("monden_face_normal");
        if (nrm != null && m.HasProperty("_BumpMap"))
        {
            m.SetTexture("_BumpMap", nrm);
            m.SetFloat("_BumpScale", normalStrength);
            m.EnableKeyword("_NORMALMAP");
        }

        var ao = skinOcclusionMap != null ? skinOcclusionMap : LoadTex("monden_face_ao");
        if (ao != null && m.HasProperty("_OcclusionMap"))
        {
            m.SetTexture("_OcclusionMap", ao);
            m.SetFloat("_OcclusionStrength", occlusionStrength);
            m.EnableKeyword("_OCCLUSIONMAP");
        }

        // 粗さは一枚の画像で配る。額と鼻筋は照り、頬と目のまわりはマットになる。
        var msk = skinMaskMap != null ? skinMaskMap : LoadTex("monden_face_mask");
        if (msk != null && m.HasProperty("_MetallicGlossMap"))
        {
            m.SetTexture("_MetallicGlossMap", msk);
            m.SetFloat("_Smoothness", 1f);            // 画像側の値をそのまま使う
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SmoothnessTextureChannel", 0f);   // アルファから読む
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.24f);

        return m;
    }

    static Texture2D LoadTex(string name)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/" + name + ".png");
#else
        return null;
#endif
    }

    /// <summary>
    /// 肌のテクスチャ。UV は球面投影で、u=0.5 が顔の正面にあたる。
    /// 一様な肌色は作り物に見えるので、顔の中心にわずかな赤みを置き、
    /// 細かいむらを重ねて、面ごとの明るさに幅を持たせている。
    /// </summary>
    Texture2D GenerateSkinTexture()
    {
        int w = Mathf.Max(256, skinTextureWidth), h = w / 2;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        tex.wrapModeU = TextureWrapMode.Repeat;
        tex.wrapModeV = TextureWrapMode.Clamp;

        var px = new Color[w * h];
        float o = 137.7f;

        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;

                // 細かいむら
                float fine = Mathf.PerlinNoise(u * 220f + o, v * 110f + o) * 0.5f
                           + Mathf.PerlinNoise(u * 60f + o, v * 30f + o) * 0.5f;
                Color c = Color.Lerp(skinShadow, skinBase, 0.55f + fine * 0.45f);

                // 顔の正面（u=0.5）にわずかな赤み
                float face = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.16f, 2f))
                           * Mathf.Exp(-Mathf.Pow((v - 0.52f) / 0.22f, 2f));
                c = Color.Lerp(c, skinFlush, face * 0.35f);

                // 下（首もと）は落とす
                c = Color.Lerp(skinShadow, c, Mathf.Clamp01((v - 0.08f) / 0.25f));

                px[y * w + x] = c;
            }
        }

        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

}
