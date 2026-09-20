using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// proto1「抜け毛テラフォーミング」の組み立て役。
///
/// 検証したいのは一点だけ。落ちてくる毛の行き先に先回りして静電気の線を張り、
/// 掬い、滑らせ、別の場所へ運ぶ——その操作が面白いかどうか。
/// なので世界は最小限にしてある。星と、周回する頭と、落ちる毛。
/// 蓄積も世代交代もエンディングも、この感触が良ければ後から足せる。
/// </summary>
public class ProtoTerraform : MonoBehaviour
{
    [Header("参照（シーンで割り当てる）")]
    public GameObject headModel;
    public Texture2D  earthTexture;
    public Texture2D  faceTexture;

    [Header("星")]
    public float globeRadius = 2.0f;
    public float earthSpinDeg = 2.0f;

    [Header("頭衛星")]
    public int   headCount = 4;
    public float orbitRadius = 4.2f;
    public float orbitSpeedDeg = 8f;
    public float headSize = 0.55f;
    [Tooltip("頭一つが持つ毛の本数。")]
    public int   hairPerHead = 40;

    [Header("落下")]
    // 落下の速さは、この体験の土台になる。
    // 速いと、落ちてくる毛への反射神経の勝負になる。遅いと、どこへ落ちるかを読んで
    // 先に構造を組む時間が生まれる。後者を見たいので、終端速度をかなり低くしてある。
    // 終端速度 ≒ gravity / drag。0.25 / 2.2 ≒ 0.11 で、周回高度から地表まで20秒ほど。
    [Tooltip("星の中心へ向かう加速度。小さいほどゆっくり沈む。")]
    public float gravity = 0.25f;
    [Tooltip("空気抵抗。大きいほどすぐ終端速度になり、ふわりと落ちる。")]
    public float drag = 2.2f;
    [Tooltip("自然に毛が抜ける間隔（秒）。")]
    public float shedInterval = 0.6f;
    public float hairLength = 0.22f;
    public float hairThickness = 0.006f;

    [Header("見た目")]
    public Color hairColor = new Color(0.06f, 0.05f, 0.05f);
    public Color skinColor = new Color(0.90f, 0.76f, 0.66f);

    Transform _globe;
    Material _hairMat, _skinMat;
    readonly List<Transform> _heads = new List<Transform>();
    readonly List<List<GameObject>> _scalp = new List<List<GameObject>>();
    readonly List<Vector3> _axes = new List<Vector3>();
    readonly List<float>   _angles = new List<float>();
    float _shedTimer;
    Transform _hairRoot;

    [Header("背景")]
    [Tooltip("宇宙の暗さ。加算合成のもやは明るい背景に埋もれるので、暗いほうが素直に見える。")]
    public Color spaceColor = new Color(0.016f, 0.020f, 0.035f);
    public bool  drawStars = true;
    public int   starCount = 700;

    void Start()
    {
        Random.InitState(20260906);
        BuildSpace();
        BuildMaterials();
        BuildGlobe();
        BuildHeads();

        _hairRoot = new GameObject("FallingHairs").transform;

        // 静電気ラインの場
        var lineGo = new GameObject("StaticLineField");
        var field = lineGo.AddComponent<StaticLineField>();
        field.center = transform.position;
        field.shellRadius = Mathf.Lerp(globeRadius, orbitRadius, 0.55f);
        field.ProtoGravityHint = gravity;
        field.targetCamera = Camera.main;

        // 旅路レポート
        new GameObject("ProtoJourneyLog").AddComponent<ProtoJourneyLog>();

        // カメラ
        var cam = Camera.main;
        if (cam != null)
        {
            var rig = cam.gameObject.GetComponent<ProtoCameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<ProtoCameraRig>();
            rig.pivot = transform.position;
            rig.distance = orbitRadius * 1.9f;
        }
    }

    /// <summary>
    /// 背景を宇宙にする。明るい空だと、加算合成の白いもやが背景に溶けて位置が読めない。
    /// 概念としても、頭が周回する先は空ではなく宇宙のほうが素直。
    /// </summary>
    void BuildSpace()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = spaceColor;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.13f, 0.14f, 0.18f);
        RenderSettings.fog = false;

        // 見えている面がほぼ照らされ、縁に明暗の境目が残る向き。
        // 真横から当てると半分が沈んで、落ちてくる毛が読めなくなる。
        var lightGo = GameObject.Find("Directional Light");
        if (lightGo != null)
        {
            var l = lightGo.GetComponent<Light>();
            lightGo.transform.rotation = Quaternion.Euler(20f, -125f, 0f);
            l.intensity = 1.9f;
            l.color = new Color(1f, 0.97f, 0.92f);
        }

        if (!drawStars) return;
        // 遠くに散らす小さな点。距離の手がかりになり、視点を回したときに星が動く。
        var starMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        starMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 1f));
        var root = new GameObject("Stars").transform;
        root.SetParent(transform, false);
        var mesh = BuildPointCloud(starCount, 60f);
        var go = new GameObject("StarPoints");
        go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = starMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>半径 r の球面に散らした、極小の四面体の集合。点として見える。</summary>
    static Mesh BuildPointCloud(int n, float r)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();
        for (int i = 0; i < n; i++)
        {
            Vector3 c = Random.onUnitSphere * r;
            float s = Random.Range(0.04f, 0.14f);
            int b = verts.Count;
            verts.Add(c + new Vector3(-s, -s, 0));
            verts.Add(c + new Vector3( s, -s, 0));
            verts.Add(c + new Vector3( 0,  s, 0));
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);   // 裏からも見えるように
        }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateBounds();
        return m;
    }

    void BuildMaterials()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        _hairMat = new Material(lit);
        _hairMat.SetColor("_BaseColor", hairColor);
        _hairMat.SetFloat("_Smoothness", 0.45f);

        _skinMat = new Material(lit);
        _skinMat.SetColor("_BaseColor", faceTexture != null ? Color.white : skinColor);
        if (faceTexture != null) _skinMat.SetTexture("_BaseMap", faceTexture);
        _skinMat.SetFloat("_Smoothness", 0.22f);
    }

    void BuildGlobe()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Globe";
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * (globeRadius * 2f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (earthTexture != null) mat.SetTexture("_BaseMap", earthTexture);
        else mat.SetColor("_BaseColor", new Color(0.16f, 0.22f, 0.30f));
        mat.SetFloat("_Smoothness", 0.12f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        _globe = go.transform;
    }

    void BuildHeads()
    {
        for (int i = 0; i < headCount; i++)
        {
            var root = new GameObject("Head" + i);
            root.transform.SetParent(transform, false);

            GameObject vis;
            if (headModel != null)
            {
                vis = Instantiate(headModel, root.transform);
                foreach (var r in vis.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int k = 0; k < mats.Length; k++) mats[k] = _skinMat;
                    r.sharedMaterials = mats;
                }
                vis.transform.localScale = Vector3.one * headSize;
            }
            else
            {
                vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                vis.transform.SetParent(root.transform, false);
                vis.transform.localScale = Vector3.one * headSize;
                vis.GetComponent<MeshRenderer>().sharedMaterial = _skinMat;
                var c = vis.GetComponent<Collider>(); if (c != null) Destroy(c);
            }

            // 頭皮の毛。抜けるとここから減っていく。
            var strands = new List<GameObject>();
            float rHead = headSize * 0.5f;
            for (int j = 0; j < hairPerHead; j++)
            {
                Vector3 d = RandomScalpDir();
                var s = new GameObject("Scalp" + j);
                s.transform.SetParent(root.transform, false);
                s.transform.localPosition = d * rHead * 0.98f;
                s.transform.localRotation = Quaternion.LookRotation(d, Vector3.up);
                PlanetHair.BuildStrand(s, hairLength, hairThickness, _hairMat);
                strands.Add(s);
            }

            _heads.Add(root.transform);
            _scalp.Add(strands);
            _axes.Add(Random.onUnitSphere);
            _angles.Add(Random.Range(0f, 360f));
        }
    }

    /// <summary>頭頂寄りに毛を配る。顔の側には生やさない。</summary>
    Vector3 RandomScalpDir()
    {
        for (int i = 0; i < 24; i++)
        {
            Vector3 d = Random.onUnitSphere;
            if (d.y > -0.15f) return d.normalized;
        }
        return Vector3.up;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (_globe != null) _globe.Rotate(Vector3.up, earthSpinDeg * dt, Space.Self);

        // 頭を周回させる
        for (int i = 0; i < _heads.Count; i++)
        {
            _angles[i] += orbitSpeedDeg * dt;
            Vector3 axis = _axes[i];
            Vector3 b1 = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 b2 = Vector3.Cross(axis, b1).normalized;
            float r = _angles[i] * Mathf.Deg2Rad;
            Vector3 pos = transform.position + (b1 * Mathf.Cos(r) + b2 * Mathf.Sin(r)) * orbitRadius;
            Vector3 up = (pos - transform.position).normalized;
            Vector3 vel = (-b1 * Mathf.Sin(r) + b2 * Mathf.Cos(r)).normalized;
            _heads[i].position = pos;
            _heads[i].rotation = Quaternion.LookRotation(vel, up);
        }

        // 自然脱毛
        _shedTimer -= dt;
        if (_shedTimer <= 0f)
        {
            _shedTimer = shedInterval * Random.Range(0.7f, 1.3f);
            Shed(Random.Range(0, _heads.Count));
        }

        // スペース: 全部の頭を叩いて一斉に落とす
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame)
            for (int i = 0; i < _heads.Count; i++) { Shed(i); Shed(i); }
    }

    /// <summary>頭 i から毛を一本抜いて、落下する毛に変える。</summary>
    void Shed(int i)
    {
        if (i < 0 || i >= _scalp.Count) return;
        var strands = _scalp[i];
        if (strands.Count == 0) return;

        int k = Random.Range(0, strands.Count);
        var s = strands[k];
        strands.RemoveAt(k);
        if (s == null) return;

        Vector3 startPos = s.transform.position;
        Destroy(s);

        var go = new GameObject("Hair");
        go.transform.SetParent(_hairRoot, false);
        var hair = go.AddComponent<ProtoHair>();
        hair.ownerName = _heads[i].name;
        hair.globe = _globe;                 // 着地したら星の子になって一緒に回る

        // 抜けた瞬間は頭の周回速度を少し引き継ぐ。真下に落ちず、流れながら沈む。
        Vector3 up = (startPos - transform.position).normalized;
        Vector3 tangent = Vector3.Cross(up, _axes[i]).normalized;
        Vector3 startVel = tangent * orbitSpeedDeg * Mathf.Deg2Rad * orbitRadius * 0.6f
                         + Random.onUnitSphere * 0.05f;

        hair.Init(transform.position, globeRadius + 0.01f, startPos, startVel,
                  _hairMat, hairThickness, hairLength, gravity, drag);
    }
}
