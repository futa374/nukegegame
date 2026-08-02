using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地球に降り積もった毛を、消さずに軽く保持・描画する蓄積システム。
///
/// ■ なぜ要るか
/// 抜けた毛（PlanetHair）は着地後も個別に Update し、全着地毛と絡まり判定（O(n^2)）していた。
/// 積もるほど重くなり、maxHairs を超えると古い毛が消える＝積もらない。
/// このゲームの主眼は「毛が地球に積もっていく」ことなので、着地毛は消さずに残したい。
///
/// ■ 仕組み
/// 毛が着地した瞬間、その姿（位置・向き）だけを記録して個別オブジェクトは破棄する。
/// 記録した毛は、1本のひな型メッシュを GPU インスタンシングで一括描画する。
/// 個別の Update もコライダーも絡まり判定も無いので、数万本でもほぼ無負荷。
///
/// ひな型メッシュは、最初に引き渡された実際の毛の形をそのまま結合して作る
/// （毛の形は全て同じなので、位置と向きだけ差し替えれば同じ見た目になる）。
///
/// クリックで拾えるよう、着地毛は球面のセルに分けた空間グリッドにも登録する（段階2で使用）。
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

    [Tooltip("個別描画する上限。超えた分はこの段階では単に描画対象から溢れる（段階で圧縮予定）。")]
    public int softCap = 60000;
    [Tooltip("拾い用グリッドのセルの大きさ（地球ローカル単位）")]
    public float cellSize = 0.08f;
    [Tooltip("着地毛の大きさ。地表に落ちた距離感を出すため小さめ。1で元サイズ。")]
    public float landedScale = 0.5f;

    // 積もった毛の姿（描画用）。地球ローカル座標で持ち、毎フレーム地球の回転を掛けて描く。
    readonly List<Matrix4x4> _matrices = new List<Matrix4x4>();
    // 拾い・情報表示用のメタ（位置は地球ローカル）
    public struct Record { public Vector3 localPos; public string owner; public string birth; }
    readonly List<Record> _records = new List<Record>();
    // 空間グリッド: セル → その中の毛インデックス（地球ローカル）
    readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();

    Mesh _hairMesh;                 // ひな型（最初の毛から作る）
    Material _mat;                  // インスタンシング可のマテリアル
    readonly Matrix4x4[] _batch = new Matrix4x4[1023];

    Transform _spin;                // 地球（自転する）への参照。これに追従させる。
    bool _spinChecked;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public int Count => _records.Count;

    /// <summary>
    /// 着地した毛を1本受け取る。source はその毛の GameObject（形とマテリアルを頂く）。
    /// </summary>
    public void Add(Transform source, string owner, string birth)
    {
        if (_hairMesh == null) CaptureTemplate(source);   // 最初の1本からひな型を作る
        EnsureSpinRef();

        // 地球ローカル座標へ移して保存する。地球が回れば、描画時に一緒に回る。
        Matrix4x4 world = Matrix4x4.TRS(source.position, source.rotation, Vector3.one * landedScale);
        Matrix4x4 local = _spin != null ? _spin.worldToLocalMatrix * world : world;
        Vector3 localPos = _spin != null ? _spin.InverseTransformPoint(source.position) : source.position;

        if (_records.Count < softCap) _matrices.Add(local);
        int idx = _records.Count;
        _records.Add(new Record { localPos = localPos, owner = owner, birth = birth });
        AddToGrid(localPos, idx);
    }

    void EnsureSpinRef()
    {
        if (_spin != null || _spinChecked) return;
        _spinChecked = true;
        var es = FindAnyObjectByType<EarthSpin>();
        if (es != null) _spin = es.transform;
    }

    // 最初に着地した毛の円柱群を1メッシュへ結合し、ひな型にする（毛ローカル空間）。
    void CaptureTemplate(Transform source)
    {
        var combines = new List<CombineInstance>();
        Matrix4x4 w2l = source.worldToLocalMatrix;
        // 直下の円柱だけを結合する。絡まった別の毛（PlanetHair を持つ子）は含めない。
        foreach (Transform c in source)
        {
            if (c.GetComponent<PlanetHair>() != null) continue;
            var mf = c.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            combines.Add(new CombineInstance
            {
                mesh = mf.sharedMesh,
                transform = w2l * mf.transform.localToWorldMatrix,
                subMeshIndex = 0,
            });
        }

        _hairMesh = new Mesh { name = "LandedHairTemplate" };
        _hairMesh.CombineMeshes(combines.ToArray(), true, true);

        // マテリアルは着地毛のものを複製し、インスタンシングを有効化
        var srcRend = source.GetComponentInChildren<MeshRenderer>();
        var srcMat = srcRend != null ? srcRend.sharedMaterial : null;
        _mat = srcMat != null ? new Material(srcMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _mat.enableInstancing = true;
    }

    void LateUpdate()
    {
        if (_hairMesh == null || _mat == null || _matrices.Count == 0) return;

        // 地球の現在の姿勢を掛けて、毛を地球と一緒に回す。
        Matrix4x4 refM = _spin != null ? _spin.localToWorldMatrix : Matrix4x4.identity;

        // 1023 本ずつインスタンシング描画。影は落とさない（数万本の影は重い）。
        int n = _matrices.Count;
        for (int start = 0; start < n; start += 1023)
        {
            int c = Mathf.Min(1023, n - start);
            for (int j = 0; j < c; j++) _batch[j] = refM * _matrices[start + j];
            Graphics.DrawMeshInstanced(
                _hairMesh, 0, _mat, _batch, c, null,
                UnityEngine.Rendering.ShadowCastingMode.Off, true);
        }
    }

    // ---- 空間グリッド（段階2の拾いで使用） ----
    long CellKey(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x / cellSize);
        int y = Mathf.FloorToInt(p.y / cellSize);
        int z = Mathf.FloorToInt(p.z / cellSize);
        // 3座標を1つの long キーへ（21bitずつ）
        return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);
    }

    void AddToGrid(Vector3 p, int idx)
    {
        long k = CellKey(p);
        if (!_grid.TryGetValue(k, out var list)) { list = new List<int>(); _grid[k] = list; }
        list.Add(idx);
    }
}
