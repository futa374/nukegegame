using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// planet シーンの一切をコードで組み立てる独立コントローラ。
/// ・地球らしいテクスチャ（手続き生成：海・大陸・極冠）＋緯線経線
/// ・それぞれバラバラに旋回する複数の頭（肌色）。頭には毛が生えている。
/// ・頭ごとに毛の色(金/白/茶/黒)と形(ストレート/うねうね/ウェーブ/チリチリ)を持ち、
///   そこから同じ種類の毛が地球へ落ちていく。落ちた毛は地球のまわりを漂う。
/// ・毛が抜けるほど頭はハゲていく。カメラは左ドラッグ回転＋スクロールズーム。
/// 空の GameObject（原点）にアタッチ。既存スクリプトには一切手を加えない。
/// </summary>
public class PlanetController : MonoBehaviour
{
    [Header("地球")]
    public float globeRadius = 3f;
    public Color lineColor = Color.black;
    public int meridianCount = 6;
    public int parallelCount = 5;
    public float lineWidth = 0.03f;

    [Header("地球テクスチャ")]
    public int textureWidth = 640;
    public float continentFreq = 2.0f;
    [Range(0f, 1f)] public float seaLevel = 0.52f;
    public int earthSeed = 7;
    public Color oceanDeep = new Color(0.09f, 0.24f, 0.48f);
    public Color oceanShallow = new Color(0.19f, 0.44f, 0.62f);
    public Color landLow = new Color(0.28f, 0.52f, 0.24f);
    public Color landHigh = new Color(0.55f, 0.47f, 0.30f);
    public Color iceColor = new Color(0.93f, 0.95f, 0.97f);

    [Header("旋回する頭")]
    public int headCount = 6;
    public float orbitHeight = 0.7f;
    public float orbitSpeedDeg = 12.5f;
    public float headSize = 0.42f;
    public Color headColor = new Color(0.98f, 0.80f, 0.66f); // 肌色
    public int headSeed = 12345;
    [Tooltip("頭に生える毛の本数（これだけ抜けるとハゲる）")]
    public int headHairCount = 45;
    [Tooltip("顔テクスチャの向き調整（度）。顔が横向きなら90/180で合わせる")]
    public float faceYawOffset = 0f;

    [Header("毛")]
    public Color hairColor = new Color(0.07f, 0.07f, 0.08f); // 黒
    [Range(0f,1f)] public float hairGloss = 0.5f;            // マットな中で少し艶

    [Header("抜け毛")]
    public float shedInterval = 0.6f;
    public float hairThickness = 0.012f;
    public float hairLength = 0.22f;
    public float scalpHairLength = 0.2f;
    public float fallSpeed = 0.18f;
    public float driftSpeedMin = 4f;
    public float driftSpeedMax = 12f;
    public int maxHairs = 400;

    [Header("カメラ")]
    public bool setupCamera = true;
    public Color backgroundColor = new Color(0.22f, 0.22f, 0.23f);
    public float cameraDistance = 8.5f;

    [Header("既存シーン連携")]
    [Tooltip("falseにすると地球の生成をスキップ。既存の地球オブジェクトをそのまま使う。")]
    public bool buildGlobe = true;

    [Header("毛の再生")]
    public float regrowDelay = 5f;  // ハゲてから再生までの秒数

    [Header("花粉・チリ")]
    public int   dustCount   = 80;
    public int   pollenCount = 40;
    public Color dustColor   = new Color(0.55f, 0.50f, 0.45f);
    public Color pollenColor = new Color(0.95f, 0.85f, 0.10f);

    static readonly string[] _namePool = {
        "KENJI","MOWRY","FUTA","TAKASHI","TAROU","SAORI","HITOMI","MEGUMI"
    };

    Transform _gridRoot;
    Transform _hairsRoot;
    Material _hairMat;
    Texture2D _faceTex;
    readonly List<OrbitingHead> _heads = new List<OrbitingHead>();
    readonly List<float> _shedTimers = new List<float>();
    readonly List<float> _shedIntervals = new List<float>();
    readonly List<float> _regrowTimers = new List<float>();
    readonly List<PlanetHair> _hairs = new List<PlanetHair>();

    void Start()
    {
        if (setupCamera) SetupCameraAndLight();
        if (buildGlobe) BuildGlobe();
        BuildHairPalette();
        BuildHeads();
        var hr = new GameObject("Hairs").transform;
        hr.SetParent(transform, false);
        _hairsRoot = hr;
        SpawnDebris();
    }

    void SpawnDebris()
    {
        var root = new GameObject("Debris").transform;
        root.SetParent(transform, false);

        var dustMat   = MakeMaterial(dustColor,   false, null);
        var pollenMat = MakeMaterial(pollenColor, false, null);

        float minR = globeRadius * 1.05f;
        float maxR = globeRadius + orbitHeight * 1.8f;

        for (int i = 0; i < dustCount; i++)
            DustParticle.Spawn(root, transform.position, Random.Range(minR, maxR),
                               DustParticle.Kind.Dust, dustMat);
        for (int i = 0; i < pollenCount; i++)
            DustParticle.Spawn(root, transform.position, Random.Range(minR, maxR),
                               DustParticle.Kind.Pollen, pollenMat);
    }

    void Update()
    {
        for (int i = 0; i < _heads.Count; i++)
        {
            var head = _heads[i];
            if (head == null) continue;

            if (head.HasHair)
            {
                // 毛がある → 通常の抜け落ちサイクル
                _regrowTimers[i] = regrowDelay;
                _shedTimers[i] += Time.deltaTime;
                if (_shedTimers[i] >= _shedIntervals[i])
                {
                    _shedTimers[i] -= _shedIntervals[i];
                    Shed(head);
                }
            }
            else
            {
                // ハゲ → 再生カウントダウン
                _regrowTimers[i] -= Time.deltaTime;
                if (_regrowTimers[i] <= 0f)
                {
                    RegrowHair(head);
                    _shedTimers[i] = 0f;
                }
            }
        }
    }

    void RegrowHair(OrbitingHead head)
    {
        foreach (var h in head.scalpHairs)
            if (h != null) Destroy(h);
        head.scalpHairs.Clear();
        BuildScalpHair(head.transform, head, _hairMat);
    }

    void Shed(OrbitingHead head)
    {
        if (head == null) return;
        if (!head.RemoveOneHair()) return; // ハゲたら以降は抜けない

        Vector3 center = transform.position;
        Vector3 headPos = head.transform.position;
        Vector3 outDir = (headPos - center).normalized;
        Vector3 spawn = headPos + outDir * headSize * 0.5f;
        spawn += head.transform.right * Random.Range(-0.06f, 0.06f);

        var go = new GameObject("Hair");
        go.transform.SetParent(_hairsRoot, false);
        var hair = go.AddComponent<PlanetHair>();
        hair.ownerName       = head.personName;
        hair.birthTimeString = GameClock.Instance != null ? GameClock.Instance.TimeString : "Unknown";
        Vector3 axis = Random.onUnitSphere;
        float drift = Random.Range(driftSpeedMin, driftSpeedMax);
        hair.Init(center, globeRadius + 0.12f, spawn, head.hairMat,
                  hairThickness, hairLength, fallSpeed, drift, axis);
        _hairs.Add(hair);

        while (_hairs.Count > maxHairs)
        {
            var old = _hairs[0];
            _hairs.RemoveAt(0);
            if (old != null) Destroy(old.gameObject);
        }
    }

    // ---------------- build ----------------

    void SetupCameraAndLight()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var cgo = new GameObject("Main Camera");
            cgo.tag = "MainCamera";
            cam = cgo.AddComponent<Camera>();
            cgo.AddComponent<AudioListener>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
        cam.fieldOfView = 45f;

        var rig = cam.GetComponent<PlanetCameraRig>();
        if (rig == null) rig = cam.gameObject.AddComponent<PlanetCameraRig>();
        rig.pivot = transform.position;
        rig.distance = cameraDistance;
        rig.minDistance = globeRadius + 0.6f;
        rig.maxDistance = globeRadius * 6f;
        rig.azimuth = 180f;
        rig.elevation = 8f;

        var lgo = new GameObject("PlanetLight");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localRotation = Quaternion.Euler(35f, -30f, 0f);
        var lt = lgo.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.05f;
        lt.shadows = LightShadows.Soft;   // 影を有効化
        lt.shadowStrength = 0.55f;
    }

    void BuildGlobe()
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "Globe";
        g.transform.SetParent(transform, false);
        g.transform.localScale = Vector3.one * (globeRadius * 2f);
        Strip(g);
        g.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(Color.white, false, GenerateEarthTexture());

        var gr = new GameObject("GlobeGrid").transform;
        gr.SetParent(transform, false);
        _gridRoot = gr;

        Material lineMat = MakeMaterial(lineColor, true, null);
        float rr = globeRadius * 1.01f;
        for (int m = 0; m < meridianCount; m++)
        {
            float phi = Mathf.PI * m / meridianCount;
            Vector3 a = new Vector3(Mathf.Cos(phi), 0f, Mathf.Sin(phi));
            MakeCircle("Meridian" + m, a, Vector3.up, rr, lineMat);
        }
        for (int p = 0; p < parallelCount; p++)
        {
            float t = (parallelCount == 1) ? 0.5f : (float)p / (parallelCount - 1);
            float lat = Mathf.Lerp(-60f, 60f, t) * Mathf.Deg2Rad;
            MakeRing("Parallel" + p, Mathf.Sin(lat) * rr, Mathf.Cos(lat) * rr, lineMat);
        }
    }

    Texture2D GenerateEarthTexture()
    {
        int w = Mathf.Max(64, textureWidth);
        int h = w / 2;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float lat = (v - 0.5f) * Mathf.PI;
            float latFrac = Mathf.Abs(lat) / (Mathf.PI * 0.5f);
            float cl = Mathf.Cos(lat), sl = Mathf.Sin(lat);
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float lon = u * 2f * Mathf.PI;
                Vector3 p = new Vector3(cl * Mathf.Cos(lon), sl, cl * Mathf.Sin(lon));
                float elev = Fbm(p * continentFreq);
                Color c;
                if (elev < seaLevel)
                    c = Color.Lerp(oceanShallow, oceanDeep, Mathf.InverseLerp(seaLevel, 0f, elev));
                else
                    c = Color.Lerp(landLow, landHigh, Mathf.InverseLerp(seaLevel, 1f, elev));
                float snow = latFrac + Fbm(p * 3.3f + new Vector3(11f, 5f, 2f)) * 0.35f - 0.15f;
                if (snow > 0.95f) c = Color.Lerp(c, iceColor, Mathf.InverseLerp(0.95f, 1.15f, snow));
                px[y * w + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    float Noise3(Vector3 p)
    {
        float a = Mathf.PerlinNoise(p.x, p.y);
        float b = Mathf.PerlinNoise(p.y, p.z);
        float c = Mathf.PerlinNoise(p.z, p.x);
        float d = Mathf.PerlinNoise(p.y + 31.4f, p.x + 11.7f);
        return (a + b + c + d) * 0.25f;
    }

    float Fbm(Vector3 p)
    {
        p += new Vector3(earthSeed * 1.7f, earthSeed * 2.3f, earthSeed * 0.9f);
        float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
        for (int i = 0; i < 4; i++) { sum += amp * Noise3(p * freq); norm += amp; freq *= 2f; amp *= 0.5f; }
        return sum / norm;
    }

    void MakeCircle(string name, Vector3 axisA, Vector3 axisB, float radius, Material mat)
    {
        int seg = 96;
        var go = new GameObject(name); go.transform.SetParent(_gridRoot, false);
        var lr = go.AddComponent<LineRenderer>(); SetupLine(lr, seg, mat);
        for (int i = 0; i < seg; i++) { float th = 2f * Mathf.PI * i / seg; lr.SetPosition(i, (axisA * Mathf.Cos(th) + axisB * Mathf.Sin(th)) * radius); }
    }

    void MakeRing(string name, float y, float ringR, Material mat)
    {
        int seg = 96;
        var go = new GameObject(name); go.transform.SetParent(_gridRoot, false);
        var lr = go.AddComponent<LineRenderer>(); SetupLine(lr, seg, mat);
        for (int i = 0; i < seg; i++) { float th = 2f * Mathf.PI * i / seg; lr.SetPosition(i, new Vector3(Mathf.Cos(th) * ringR, y, Mathf.Sin(th) * ringR)); }
    }

    void SetupLine(LineRenderer lr, int seg, Material mat)
    {
        float lw = lineWidth > 0.0005f ? lineWidth : 0.025f; // 保存値が0でも見えるように
        lr.useWorldSpace = false; lr.loop = true; lr.positionCount = seg; lr.widthMultiplier = lw;
        lr.numCapVertices = 2; lr.numCornerVertices = 2; lr.material = mat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
        lr.alignment = LineAlignment.View;
    }

    void BuildHairPalette()
    {
        // 黒一種。リットで少し艶（周囲がマットなので毛だけ光る）。
        _hairMat = MakeMaterial(hairColor, false, null);
        if (_hairMat.HasProperty("_Smoothness")) _hairMat.SetFloat("_Smoothness", hairGloss);
        if (_hairMat.HasProperty("_Glossiness")) _hairMat.SetFloat("_Glossiness", hairGloss);
        _faceTex = Resources.Load<Texture2D>("planet_face");
    }

    void BuildHeads()
    {
        Random.State prev = Random.state;
        Random.InitState(headSeed);
        for (int i = 0; i < headCount; i++)
        {
            var hgo = new GameObject("OrbitingHead" + i);
            hgo.transform.SetParent(transform, false);
            var oh = hgo.AddComponent<OrbitingHead>();
            oh.center = transform.position;
            oh.radius = globeRadius + orbitHeight + Random.Range(-0.05f, 0.18f);
            oh.orbitAxis = Random.onUnitSphere;
            float dir = Random.value < 0.5f ? -1f : 1f;
            oh.speedDeg = orbitSpeedDeg * Random.Range(0.7f, 1.3f) * dir;
            oh.angleDeg = Random.Range(0f, 360f);

            oh.hairMat    = _hairMat;
            oh.personName = _namePool[i % _namePool.Length];
            oh.personAge  = Random.Range(18, 81);

            BuildHeadModel(hgo.transform);
            BuildScalpHair(hgo.transform, oh, _hairMat);

            _heads.Add(oh);
            _shedIntervals.Add(shedInterval * Random.Range(0.8f, 1.3f));
            _shedTimers.Add(Random.Range(0f, shedInterval));
            _regrowTimers.Add(regrowDelay);
        }
        Random.state = prev;
    }

    void BuildHeadModel(Transform parent)
    {
        var skull = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        skull.name = "Skull"; skull.transform.SetParent(parent, false);
        skull.transform.localScale = new Vector3(headSize * 0.9f, headSize, headSize * 0.9f);
        // 顔テクスチャ（青を除いた顔）を球に貼る。無ければ肌色。鼻は無し。
        Material headMat = _faceTex != null ? MakeMaterial(Color.white, false, _faceTex)
                                            : MakeMaterial(headColor, false, null);
        // SphereColliderはクリック検出のために残す（Stripしない）
        skull.GetComponent<MeshRenderer>().sharedMaterial = headMat;
        skull.transform.localRotation = Quaternion.Euler(0f, faceYawOffset, 0f);
    }

    void BuildScalpHair(Transform parent, OrbitingHead oh, Material mat)
    {
        float rHead = headSize * 0.5f;
        for (int j = 0; j < headHairCount; j++)
        {
            Vector3 d = RandomScalpDir();
            var root = new GameObject("ScalpHair" + j);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = d * rHead * 0.96f;
            root.transform.localRotation = Quaternion.LookRotation(d, Vector3.up);
            PlanetHair.BuildStrand(root, scalpHairLength, hairThickness, mat);
            oh.scalpHairs.Add(root);
        }
    }

    Vector3 RandomScalpDir()
    {
        for (int k = 0; k < 24; k++)
        {
            Vector3 d = Random.onUnitSphere;
            if (d.y < -0.15f) continue;                 // 下側には生やさない
            if (d.z > 0.35f && d.y < 0.35f) continue;   // 顔の正面下は避ける
            return d;
        }
        return Vector3.up;
    }

    static void Strip(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) Destroy(c); }

    Material MakeMaterial(Color c, bool unlit, Texture tex)
    {
        Shader sh = null;
        if (unlit) { sh = Shader.Find("Universal Render Pipeline/Unlit"); if (sh == null) sh = Shader.Find("Unlit/Color"); }
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (tex != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); m.mainTexture = tex; }
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
        return m;
    }
}
