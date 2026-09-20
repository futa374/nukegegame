using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地球に降り積もった毛を、消さずに軽く保持・描画する蓄積システム。
///
/// ■ なぜ要るか
/// 抜けた毛（PlanetHair）は着地後も個別に Update し、全着地毛と絡まり判定（O(n^2)）していた。
/// 積もるほど重くなり、maxHairs を超えると古い毛が消える＝積もらない。
/// このゲームの主眼は「細い毛が狂気的な量で集まり、地球の表面を覆う」ことにある。
/// 面で塗って代用するのではなく、一本一本が実際に積もって地面を隠すまで、消さずに残す。
///
/// ■ 仕組み
/// 毛が着地した瞬間、その姿（位置・向き・形・色）だけを記録して個別オブジェクトは破棄する。
/// 記録した毛は、接平面に寝た細いリボン（1本10頂点）として、色ごとのチャンクメッシュへ焼き込む。
/// チャンクは地球の子なので、自転は階層に任せて毎フレームの CPU 負荷は無い。
/// 数十万本でも、頂点数は数百万に収まる。
///
/// クリックで拾えるよう、着地毛は球面のセルに分けた空間グリッドにも登録する。
/// </summary>
[DefaultExecutionOrder(300)]
public class LandedHairField : MonoBehaviour
{
    public static LandedHairField Instance { get; private set; }

    /// <summary>無ければ作る。PlanetHair が着地時に呼ぶ。</summary>
    public static LandedHairField Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("LandedHairField");
        Instance = go.AddComponent<LandedHairField>();
        return Instance;
    }

    [Tooltip("保持する上限。超えた分は記録だけ残して描かない。")]
    public int softCap = 1000000;
    [Tooltip("拾い用グリッドのセルの大きさ（地球ローカル単位）")]
    public float cellSize = 0.08f;
    [Tooltip("着地毛の大きさ。地表に落ちた距離感を出すため小さめ。1で元サイズ。")]
    public float landedScale = 0.5f;
    [Tooltip("地面に寝た毛の幅を、落ちていたときの太さの何倍にするか。細すぎると何万本あっても地面が隠れない。")]
    public float landedWidthScale = 3.0f;
    [Tooltip("一つのチャンクに焼き込む本数。多いほどメッシュ数は減るが、作り直しが重くなる。")]
    public int hairsPerChunk = 4096;
    [Tooltip("積もりかけのチャンクを何秒おきに描き直すか。")]
    public float rebuildInterval = 0.5f;

    // 拾い・情報表示用のメタ（位置は地球ローカル）
    public struct Record { public Vector3 localPos; public string owner; public string birth; }
    readonly List<Record> _records = new List<Record>();
    readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();

    // 一本の姿。焼き込みに必要なものだけ。
    struct Strand
    {
        public Vector3 root;      // 地球ローカルの根元
        public Vector3 along;     // 毛の向き（接平面内、ローカル）
        public Vector3 normal;    // 地面の法線（ローカル）
        public float length, width, curl;
    }

    // 色ごとの束。積もりかけのチャンク（pending）と、焼き上がったチャンク（done）。
    class Batch
    {
        public Material mat;
        public readonly List<Strand> pending = new List<Strand>();
        public GameObject pendingGo; public Mesh pendingMesh;
        public bool dirty;
        public int baked;
    }
    readonly Dictionary<long, Batch> _batches = new Dictionary<long, Batch>();
    int _drawn;
    float _rebuildTimer;

    Transform _spin;                // 地球（自転する）への参照。この子に焼き込む。
    bool _spinChecked;
    Transform _root;                // チャンクの親。地球の子。

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public int Count => _records.Count;
    public int BatchCount => _batches.Count;

    /// <summary>
    /// 着地した毛を1本受け取る。source はその毛の GameObject（形とマテリアルを頂く）。
    /// </summary>
    public void Add(Transform source, string owner, string birth)
    {
        EnsureSpinRef();

        Vector3 localPos = _spin != null ? _spin.InverseTransformPoint(source.position) : source.position;
        int idx = _records.Count;
        _records.Add(new Record { localPos = localPos, owner = owner, birth = birth });
        AddToGrid(localPos, idx);

        if (_drawn >= softCap) return;
        _drawn++;

        // 形：落ちていたときの毛から。地面では少し縮め、幅は広げる。
        var ph = source.GetComponent<PlanetHair>();
        float len   = (ph != null ? ph.strandLength : 0.2f) * landedScale;
        float width = (ph != null ? ph.strandThickness : 0.01f) * landedScale * landedWidthScale;
        float curl  = ph != null ? ph.strandCurl : 1f;

        // 向き：毛のローカル Z（進行方向）を接平面へ倒す。法線は地球中心からの向き。
        Vector3 nWorld = (source.position - (_spin != null ? _spin.position : Vector3.zero)).normalized;
        Vector3 aWorld = Vector3.ProjectOnPlane(source.forward, nWorld);
        if (aWorld.sqrMagnitude < 1e-8f) aWorld = Vector3.Cross(nWorld, Vector3.up);
        if (aWorld.sqrMagnitude < 1e-8f) aWorld = Vector3.Cross(nWorld, Vector3.right);
        aWorld.Normalize();

        // ローカルへ。地球は拡大されているので、方向は回転だけ、長さは倍率で戻す。
        float s = _spin != null ? Mathf.Max(_spin.lossyScale.x, 1e-6f) : 1f;
        var st = new Strand
        {
            root   = localPos,
            along  = _spin != null ? _spin.InverseTransformDirection(aWorld) : aWorld,
            normal = _spin != null ? _spin.InverseTransformDirection(nWorld) : nWorld,
            length = len / s, width = width / s, curl = curl,
        };

        var b = BatchFor(source);
        b.pending.Add(st);
        b.dirty = true;
        if (b.pending.Count >= hairsPerChunk) Bake(b);
    }

    void EnsureSpinRef()
    {
        if (_spin != null || _spinChecked) return;
        _spinChecked = true;
        var es = FindAnyObjectByType<EarthSpin>();
        if (es != null) _spin = es.transform;
        var rootGo = new GameObject("LandedHairChunks");
        _root = rootGo.transform;
        _root.SetParent(_spin != null ? _spin : transform, false);
        _root.localPosition = Vector3.zero; _root.localRotation = Quaternion.identity; _root.localScale = Vector3.one;
    }

    /// <summary>この毛の見た目（色・艶）に合う束を返す。無ければ、その毛のマテリアルから作る。</summary>
    Batch BatchFor(Transform source)
    {
        var rend = source.GetComponentInChildren<MeshRenderer>();
        var src = rend != null ? rend.sharedMaterial : null;
        long key = MaterialKey(src);
        if (_batches.TryGetValue(key, out var b)) return b;

        var mat = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        // リボンは裏からも見える。両面描画。
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        mat.doubleSidedGI = true;
        b = new Batch { mat = mat };
        _batches[key] = b;
        return b;
    }

    /// <summary>色と艶を量子化して束の鍵にする。同じ髪型の頭はマテリアルの実体が別でも、同じ束に入る。</summary>
    static long MaterialKey(Material m)
    {
        if (m == null) return 0;
        Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.black;
        float s = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0f;
        long r = (long)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f);
        long g = (long)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f);
        long bl = (long)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
        long sm = (long)Mathf.RoundToInt(Mathf.Clamp01(s) * 63f);
        return (r << 24) | (g << 16) | (bl << 8) | sm;
    }

    // ------------------------------------------------------------------
    // 焼き込み
    // ------------------------------------------------------------------

    static readonly List<Vector3> _v = new List<Vector3>();
    static readonly List<Vector3> _n = new List<Vector3>();
    static readonly List<int>     _t = new List<int>();
    const int PTS = 5;   // リボンの折れ点。10頂点・8三角形

    void LateUpdate()
    {
        _rebuildTimer -= Time.deltaTime;
        if (_rebuildTimer > 0f) return;
        _rebuildTimer = rebuildInterval;
        foreach (var kv in _batches)
        {
            var b = kv.Value;
            if (!b.dirty) continue;
            RebuildPending(b);
            b.dirty = false;
        }
    }

    /// <summary>積もりかけのチャンクを描き直す。</summary>
    void RebuildPending(Batch b)
    {
        if (b.pendingGo == null)
        {
            b.pendingGo = NewChunkGo(b, "Chunk_pending");
            b.pendingMesh = new Mesh { name = "LandedHairChunk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            b.pendingGo.GetComponent<MeshFilter>().sharedMesh = b.pendingMesh;
        }
        FillMesh(b.pendingMesh, b.pending);
    }

    /// <summary>満杯になったチャンクを確定し、次のチャンクを始める。</summary>
    void Bake(Batch b)
    {
        if (b.pendingGo == null)
        {
            b.pendingGo = NewChunkGo(b, "Chunk");
            b.pendingMesh = new Mesh { name = "LandedHairChunk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            b.pendingGo.GetComponent<MeshFilter>().sharedMesh = b.pendingMesh;
        }
        FillMesh(b.pendingMesh, b.pending);
        b.pendingMesh.UploadMeshData(true);          // 以後は触らない。GPU 側だけに残す。
        b.pendingGo.name = "Chunk_" + b.baked;
        b.baked++;
        b.pending.Clear();
        b.pendingGo = null; b.pendingMesh = null; b.dirty = false;
    }

    GameObject NewChunkGo(Batch b, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = b.mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 数十万本の影は重い
        mr.receiveShadows = false;
        return go;
    }

    /// <summary>毛の列をリボンのメッシュへ。地球ローカル座標。</summary>
    static void FillMesh(Mesh mesh, List<Strand> strands)
    {
        _v.Clear(); _n.Clear(); _t.Clear();
        foreach (var s in strands) AppendRibbon(s);
        mesh.Clear();
        mesh.SetVertices(_v);
        mesh.SetNormals(_n);
        mesh.SetTriangles(_t, 0);
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// 一本を、接平面に寝たリボンにする。芯線は落ちていたときと同じうねり（PointOnStrand）を
    /// 接平面へ投影したもの。幅は接平面内で芯線に直交する向き。上からも横からも見える。
    /// </summary>
    static void AppendRibbon(Strand s)
    {
        Vector3 side = Vector3.Cross(s.normal, s.along).normalized;
        int b = _v.Count;
        float halfW = s.width * 0.5f;
        for (int i = 0; i < PTS; i++)
        {
            float t = (float)i / (PTS - 1);
            // 芯線：PointOnStrand は (x,y,z)=（横うねり, 縦うねり, 進行）。横を side、進行を along へ。
            Vector3 p = PlanetHair.PointOnStrand(t, s.length, s.curl);
            Vector3 c = s.root + s.along * p.z + side * p.x + s.normal * (Mathf.Abs(p.y) * 0.3f);
            // 毛先へ細く
            float w = halfW * Mathf.Lerp(1f, 0.35f, t);
            _v.Add(c - side * w); _n.Add(s.normal);
            _v.Add(c + side * w); _n.Add(s.normal);
        }
        for (int i = 0; i < PTS - 1; i++)
        {
            int a0 = b + i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
            _t.Add(a0); _t.Add(b0); _t.Add(a1);
            _t.Add(a1); _t.Add(b0); _t.Add(b1);
        }
    }

    // ---- 空間グリッド（拾いで使用） ----
    long CellKey(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x / cellSize);
        int y = Mathf.FloorToInt(p.y / cellSize);
        int z = Mathf.FloorToInt(p.z / cellSize);
        return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
    }

    void AddToGrid(Vector3 p, int idx)
    {
        long k = CellKey(p);
        if (!_grid.TryGetValue(k, out var list)) { list = new List<int>(); _grid[k] = list; }
        list.Add(idx);
    }
}
