using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 一本の毛が、画面の上から下へ、空気に押し返されながら舞い降りてゆく幕間。
///
/// ■ なぜ揺れるのか（この実装の芯）
/// 細長い繊維の空気抵抗は等方的ではない。軸に垂直な向きの運動は強く抵抗を受け、
/// 軸に沿った向きの運動はその半分ほどしか受けない。
/// この差があるために、少しでも傾いた毛は、重力で下へ引かれながら、
/// 抵抗の小さい「軸に沿う向き」へ逃げる。つまり斜めに滑る。滑れば姿勢が変わり、
/// 姿勢が変われば滑る向きも変わる。舞い落ちるという運動は、
/// 揺らそうとして揺れているのではなく、この非対称から勝手に出てくる。
///
/// そのため、ここでは軌道を一切書いていない。毛を質点の鎖として持ち、
/// 各点に重力・異方的な空気抵抗・ごく弱い空気の流れを与えて、Verlet 積分で解いているだけである。
/// 前の実装はサインカーブで横に振っていたが、それは結果の形だけを真似たもので、
/// 速さと姿勢の対応がないため、見ていて嘘になる。
///
/// ■ 画面
/// スマートフォンの縦位置。9:16 の枠を画面中央に作り、外側は塞ぐ。
/// カメラは平行投影。遠近が付くと落下が「遠ざかること」に見えてしまうため。
///
/// 既存スクリプトには手を加えない独立ファイル。空の GameObject にアタッチして使う。
/// </summary>
public class HairFallingInterlude : MonoBehaviour
{
    [Header("画面（スマホ縦）")]
    [Tooltip("横 : 縦 の比。0.5625 = 9:16")]
    public float portraitAspect = 9f / 16f;
    public Color letterboxColor = Color.black;
    public Color backgroundColor = new Color(0.95f, 0.94f, 0.92f);
    [Tooltip("見えている範囲の高さ（ワールド単位）の半分")]
    public float viewHalfHeight = 3.2f;

    [Header("毛")]
    public Color hairColor = new Color(0.11f, 0.10f, 0.09f);
    public float hairLength = 1.5f;
    public float hairThickness = 0.045f;
    [Tooltip("質点の数。多いほどしなやかに曲がるが、重くなる。")]
    [Range(4, 24)] public int nodes = 10;
    [Tooltip("曲がりにくさ。0でひも、1でほぼ真っ直ぐ。")]
    [Range(0f, 1f)] public float stiffness = 0.35f;
    [Tooltip("生まれつきの反り。真っ直ぐな毛は抵抗が軸対称になり、ただ滑り落ちるだけになる。\n反りがあると抵抗の中心が軸からずれ、姿勢を返すトルクが生まれて舞う。")]
    [Range(0f, 1f)] public float curl = 0.45f;

    [Header("空気")]
    [Tooltip("真横を向いた毛が落ちるときの終端速度。小さいほどゆっくり舞う。")]
    public float terminalSpeed = 0.72f;
    [Tooltip("軸に沿う向きの抵抗の割合。細長い物体では 0.5 前後。\n1にすると等方になり、まっすぐ落ちるだけになる。")]
    [Range(0.15f, 1f)] public float axialDragRatio = 0.5f;
    [Tooltip("空気の渦の強さ。0にすると、毛は真横を向いてまっすぐ沈むだけになる。")]
    public float windStrength = 0.42f;
    [Tooltip("渦の細かさ（小さいほど大きなうねり）")]
    public float windScale = 0.30f;
    [Tooltip("空気の流れが移り変わる速さ")]
    public float windSpeed = 0.25f;
    [Tooltip("重力加速度")]
    public float gravity = 9.81f;
    [Tooltip("速度の二乗に比例する抵抗の割合。\n完全な低レイノルズ数なら0だが、毛の落下はその境目あたりにあり、少し混ぜた方が動きが生々しい。")]
    [Range(0f, 1.5f)] public float quadraticDrag = 0.55f;

    [Header("着地")]
    [Tooltip("画面中央に着地するよう、落とし始めの位置を選ぶ。力は加えない。")]
    public bool aimForCenter = true;
    [Tooltip("中央からこれだけ以内に落ちれば良しとする")]
    public float centerTolerance = 0.12f;
    [Tooltip("落とし直して狙いを合わせる回数")]
    [Range(1, 12)] public int aimAttempts = 6;

    [Header("床")]
    public bool showFloor = true;
    public Color floorColor = new Color(0.86f, 0.84f, 0.81f);
    [Tooltip("床に着いてから、次が落ちてくるまでの間（秒）")]
    public float restSeconds = 1.8f;

    [Header("繰り返しと接続")]
    public bool loop = true;
    [Tooltip("落ちきったあとに読み込むシーン名（空なら遷移しない）")]
    public string nextSceneName = "";
    public UnityEvent onLanded;
    public bool allowSkip = true;

    // ------------------------------------------------------------------
    // 内部
    // ------------------------------------------------------------------

    Camera _cam, _letterboxCam;
    Transform _hairRoot;
    Transform[] _segments;

    Vector3[] _p;        // 現在位置
    Vector3[] _prev;     // 前フレーム位置（Verlet ではこれが速度を兼ねる）
    float _spacing;

    float _restT = -1f;
    float _windSeed;
    float _simTime;      // 風の時計。実時間ではなくこちらを使うことで、落下を予測できる（＝再現できる）ようにする
    float _accumulator;
    bool _finished;

    const float FixedStep = 1f / 120f;   // 抵抗が効く運動なので、細かく刻んだ方が安定する

    void Start()
    {
        BuildCameras();
        BuildFloor();
        BuildHair();
        Restart();
    }

    void Update()
    {
        ApplyPortraitRect();

        if (allowSkip && (ClickedThisFrame() || EscapedThisFrame())) { Land(skip: true); return; }

        if (_restT >= 0f)
        {
            _restT += Time.deltaTime;
            if (_restT >= restSeconds && loop) Restart();
            return;
        }

        // 可変フレームレートのまま解くと抵抗の効きが変わってしまうので、固定刻みで進める
        _accumulator += Mathf.Min(Time.deltaTime, 0.1f);
        while (_accumulator >= FixedStep)
        {
            Step(FixedStep);
            _accumulator -= FixedStep;
        }

        ApplyToSegments();
        CheckLanded();
    }

    // ------------------------------------------------------------------
    // 物理
    // ------------------------------------------------------------------

    void Step(float dt)
    {
        _simTime += dt;
        int n = _p.Length;

        for (int i = 0; i < n; i++)
        {
            Vector3 v = (_p[i] - _prev[i]) / dt;

            // その点における毛の向き（接線）。隣の点との差から取る。
            Vector3 tangent = Tangent(i);

            // 空気に対する相対速度。空気そのものもゆっくり流れている。
            Vector3 rel = v - Wind(_p[i]);

            // ここが要。速度を接線方向と垂直方向に分け、別々の係数で抵抗を掛ける。
            // 垂直成分の方が強く抵抗を受けるので、傾いた毛は「軸に沿う向き」へ滑り出す。
            Vector3 axial = Vector3.Dot(rel, tangent) * tangent;
            Vector3 normal = rel - axial;

            float kPerp = gravity / Mathf.Max(0.01f, terminalSpeed);   // 終端速度から逆算した抵抗係数
            float kAxial = kPerp * axialDragRatio;

            // 線形（低レイノルズ数）に、二次の項を少し混ぜる。
            // 二次の項は速いときほど強く効くので、滑り出しが行き過ぎずに姿勢が返りやすくなる。
            float q = quadraticDrag / Mathf.Max(0.01f, terminalSpeed);
            Vector3 dragN = normal * kPerp * (1f + q * normal.magnitude);
            Vector3 dragA = axial * kAxial * (1f + q * axial.magnitude);

            Vector3 accel = new Vector3(0f, -gravity, 0f) - (dragN + dragA);

            Vector3 next = _p[i] + (_p[i] - _prev[i]) + accel * dt * dt;
            _prev[i] = _p[i];
            _p[i] = next;
        }

        // 長さを保つ（毛は伸び縮みしない）
        for (int it = 0; it < 4; it++) SolveDistance();

        // 曲がりにくさ。実際の毛には腰があり、完全なひもではない。
        if (stiffness > 0f) SolveBending();

        SolveFloor();
    }

    Vector3 Tangent(int i)
    {
        int a = Mathf.Max(0, i - 1);
        int b = Mathf.Min(_p.Length - 1, i + 1);
        Vector3 t = _p[b] - _p[a];
        return t.sqrMagnitude > 1e-10f ? t.normalized : Vector3.up;
    }

    void SolveDistance()
    {
        for (int i = 0; i < _p.Length - 1; i++)
        {
            Vector3 d = _p[i + 1] - _p[i];
            float len = d.magnitude;
            if (len < 1e-6f) continue;
            float diff = (len - _spacing) / len;
            Vector3 corr = d * (diff * 0.5f);
            _p[i] += corr;
            _p[i + 1] -= corr;
        }
    }

    void SolveBending()
    {
        // 一つ飛ばしの点どうしを、「その毛の本来の反り」の距離へ向けて弱く引く。
        // 真っ直ぐ（rest = 2*spacing）にすると反りが消え、舞わなくなる。
        float restAngle = curl * 24f * Mathf.Deg2Rad;      // 節ごとの曲がり角
        float rest = 2f * _spacing * Mathf.Cos(restAngle * 0.5f);
        float k = stiffness * 0.5f;
        for (int i = 0; i < _p.Length - 2; i++)
        {
            Vector3 d = _p[i + 2] - _p[i];
            float len = d.magnitude;
            if (len < 1e-6f) continue;
            float diff = (len - rest) / len;
            Vector3 corr = d * (diff * 0.5f * k);
            _p[i] += corr;
            _p[i + 2] -= corr;
        }
    }

    void SolveFloor()
    {
        float floorY = FloorY() + hairThickness * 0.5f;
        for (int i = 0; i < _p.Length; i++)
        {
            if (_p[i].y >= floorY) continue;
            _p[i].y = floorY;
            // 床では横滑りを止める（摩擦）
            Vector3 v = _p[i] - _prev[i];
            v.x *= 0.4f;
            v.y = 0f;
            v.z *= 0.4f;
            _prev[i] = _p[i] - v;
        }
    }

    /// <summary>
    /// 部屋の空気。渦として与える。
    ///
    /// 毛それ自体は、空気抵抗が強く効くために、やがて真横を向いた姿勢で安定してしまう。
    /// 現実の毛がいつまでも舞っていられるのは、毛が揺れているからではなく、空気の側が動いているからである。
    /// そこで風を、ノイズをそのまま速度にするのではなく、
    /// スカラー場の勾配を直交させた（＝回転させた）形で作る。
    /// こうすると場に湧き出しが無くなり、押し流すのではなく巻き込む流れになる。
    /// 毛は渦に乗って向きを変えられ、向きが変われば滑る方向も変わる。
    /// </summary>
    Vector3 Wind(Vector3 at)
    {
        if (windStrength <= 0f) return Vector3.zero;

        float t = _simTime * windSpeed;
        const float e = 0.15f;   // 勾配を測る幅

        // スカラー場 ψ の勾配を 90 度回して、湧き出しの無い（非圧縮な）流れにする
        float psiUp    = Psi(at.x, at.y + e, t);
        float psiDown  = Psi(at.x, at.y - e, t);
        float psiRight = Psi(at.x + e, at.y, t);
        float psiLeft  = Psi(at.x - e, at.y, t);

        float vx = (psiUp - psiDown) / (2f * e);
        float vy = -(psiRight - psiLeft) / (2f * e);

        return new Vector3(vx, vy, 0f) * windStrength;
    }

    float Psi(float x, float y, float t)
    {
        return Mathf.PerlinNoise(x * windScale + _windSeed, y * windScale + t)
             + 0.5f * Mathf.PerlinNoise(x * windScale * 2.3f + _windSeed + 17f, y * windScale * 2.3f - t * 1.4f);
    }

    // ------------------------------------------------------------------
    // 進行
    // ------------------------------------------------------------------

    void Restart()
    {
        _finished = false;
        _restT = -1f;
        _accumulator = 0f;
        _windSeed = Random.Range(0f, 500f);

        float halfWidth = viewHalfHeight * portraitAspect;
        float startY = viewHalfHeight + hairLength * 0.6f;

        // 少し傾けて置く。真っ直ぐ垂直だと抵抗の非対称が働かず、まっすぐ落ちてしまう。
        float tilt = Random.Range(35f, 80f) * (Random.value < 0.5f ? -1f : 1f);

        float startX = Random.Range(-halfWidth * 0.4f, halfWidth * 0.4f);

        if (aimForCenter)
        {
            // 落とし始めの位置をどう選んでも中央に来ない風の巡り合わせもある。
            // その場合は空気の具合そのものを選び直す。どの瞬間の空気を描くか、という選択にあたる。
            float bestX = 0f, bestError = float.MaxValue, bestSeed = _windSeed;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                float error;
                float x = SolveStartX(startY, tilt, halfWidth, out error);
                if (error < bestError) { bestError = error; bestX = x; bestSeed = _windSeed; }
                if (bestError <= centerTolerance) break;
                _windSeed = Random.Range(0f, 500f);
            }
            _windSeed = bestSeed;
            startX = bestX;
        }

        InitShape(startX, startY, tilt);
        ApplyToSegments();
    }

    /// <summary>
    /// 中央に着地させるために、落とす位置の方を決める。
    ///
    /// 毛を中央へ引き戻す力を足すのが手軽だが、それは見えない紐で操ることになり、
    /// せっかく物理で解いている意味が無くなる。ここでは力には一切触れず、
    /// 空気の流れが決まっている以上 落下は再現できることを使って、
    /// 実際に落としてみて、ずれた分だけ落とし始めの位置をずらす、を繰り返す。
    /// 風にどう流されるかを見越して、風上から落とす、という手つきになる。
    /// </summary>
    float SolveStartX(float startY, float tilt, float halfWidth, out float resultError)
    {
        float saveSimTime = _simTime;

        float best = 0f;
        float bestError = float.MaxValue;

        // まず粗く見渡す。
        // 風は場所ごとに渦の向きが違うので、着地点は落とし始めの位置になめらかに応じない。
        // ずれた分だけ戻す、という補正だけでは、渦をまたいだ途端に迷子になる。
        const int coarse = 7;
        for (int i = 0; i < coarse; i++)
        {
            float x = Mathf.Lerp(-halfWidth * 1.2f, halfWidth * 1.2f, (float)i / (coarse - 1));
            _simTime = saveSimTime;
            float landed = PredictLandingX(x, startY, tilt);
            float error = Mathf.Abs(landed);
            if (error < bestError) { bestError = error; best = x; }
        }

        // そのうえで、いちばん近かった辺りを詰める。
        // ここでは補正を効かせすぎない（行き過ぎると別の渦に入ってしまう）。
        float candidate = best;
        for (int k = 0; k < Mathf.Max(1, aimAttempts); k++)
        {
            if (bestError <= centerTolerance) break;

            _simTime = saveSimTime;
            float landed = PredictLandingX(candidate, startY, tilt);
            float error = Mathf.Abs(landed);
            if (error < bestError) { bestError = error; best = candidate; }

            candidate -= landed * 0.7f;
            candidate = Mathf.Clamp(candidate, -halfWidth * 1.6f, halfWidth * 1.6f);
        }

        _simTime = saveSimTime;
        resultError = bestError;
        return best;
    }

    /// <summary>その位置から落としたら、どこに着地するかを実際に解いて調べる。</summary>
    float PredictLandingX(float startX, float startY, float tilt)
    {
        InitShape(startX, startY, tilt);

        float floorY = FloorY() + hairThickness * 0.5f;
        int maxSteps = Mathf.CeilToInt(45f / FixedStep);

        for (int i = 0; i < maxSteps; i++)
        {
            Step(FixedStep);

            float lowest = float.MaxValue;
            for (int j = 0; j < _p.Length; j++) lowest = Mathf.Min(lowest, _p[j].y);
            if (lowest <= floorY + 0.002f) break;
        }

        float cx = 0f;
        for (int j = 0; j < _p.Length; j++) cx += _p[j].x;
        return cx / _p.Length;
    }

    /// <summary>反りを持たせた円弧として、指定の位置・傾きに置く。初速はゼロ。</summary>
    void InitShape(float startX, float startY, float tilt)
    {
        Quaternion rot = Quaternion.Euler(0f, 0f, tilt);

        float restAngle = curl * 24f * Mathf.Deg2Rad;
        Vector3 pos = Vector3.zero;
        Vector3 dir = new Vector3(0f, 1f, 0f);
        var shape = new Vector3[_p.Length];
        for (int i = 0; i < _p.Length; i++)
        {
            shape[i] = pos;
            pos += dir * _spacing;
            dir = Quaternion.Euler(0f, 0f, restAngle * Mathf.Rad2Deg) * dir;
        }
        Vector3 center = Vector3.zero;
        for (int i = 0; i < shape.Length; i++) center += shape[i];
        center /= shape.Length;

        for (int i = 0; i < _p.Length; i++)
        {
            _p[i] = new Vector3(startX, startY, 0f) + rot * (shape[i] - center);
            _prev[i] = _p[i];
        }
    }

    void CheckLanded()
    {
        float floorY = FloorY() + hairThickness * 0.5f;
        float speed = 0f;
        bool allDown = true;

        for (int i = 0; i < _p.Length; i++)
        {
            speed += (_p[i] - _prev[i]).magnitude;
            if (_p[i].y > floorY + hairThickness * 2f) allDown = false;
        }

        if (allDown && speed / _p.Length < 0.0006f) Land(skip: false);

        // 横へ滑って枠から出てしまった場合も、そこで一区切りにする
        float halfWidth = viewHalfHeight * portraitAspect + hairLength;
        if (Mathf.Abs(_p[0].x) > halfWidth) Land(skip: true);
    }

    void Land(bool skip)
    {
        if (_finished) return;
        _finished = true;
        _restT = 0f;

        onLanded?.Invoke();

        if (!skip && !string.IsNullOrEmpty(nextSceneName))
            SceneManager.LoadScene(nextSceneName);
    }

    // ------------------------------------------------------------------
    // 見た目
    // ------------------------------------------------------------------

    void ApplyToSegments()
    {
        if (_segments == null) return;
        for (int i = 0; i < _segments.Length; i++)
        {
            Vector3 a = _p[i], b = _p[i + 1];
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-6f) continue;

            var s = _segments[i];
            s.position = (a + b) * 0.5f;
            s.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
            float thick = hairThickness * Mathf.Lerp(1f, 0.75f, (float)i / Mathf.Max(1, _segments.Length - 1));
            s.localScale = new Vector3(thick, len * 0.5f, thick);
        }
    }

    // ------------------------------------------------------------------
    // 組み立て
    // ------------------------------------------------------------------

    void BuildCameras()
    {
        var lb = new GameObject("LetterboxCamera");
        lb.transform.SetParent(transform, false);
        _letterboxCam = lb.AddComponent<Camera>();
        _letterboxCam.clearFlags = CameraClearFlags.SolidColor;
        _letterboxCam.backgroundColor = letterboxColor;
        _letterboxCam.cullingMask = 0;
        _letterboxCam.depth = -100;
        _letterboxCam.rect = new Rect(0f, 0f, 1f, 1f);

        _cam = Camera.main;
        if (_cam == null)
        {
            var go = new GameObject("FallCamera");
            go.transform.SetParent(transform, false);
            go.tag = "MainCamera";
            _cam = go.AddComponent<Camera>();
        }
        _cam.transform.position = new Vector3(0f, 0f, -10f);
        _cam.transform.rotation = Quaternion.identity;
        _cam.orthographic = true;
        _cam.orthographicSize = viewHalfHeight;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = backgroundColor;
        _cam.depth = 0;

        var light = new GameObject("KeyLight");
        light.transform.SetParent(transform, false);
        light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
        var lt = light.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.05f;
    }

    float FloorY() { return -viewHalfHeight * 0.82f; }

    void BuildFloor()
    {
        if (!showFloor) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Floor";
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0f, FloorY() - viewHalfHeight * 0.5f, 0.5f);
        go.transform.localScale = new Vector3(viewHalfHeight * 4f, viewHalfHeight, 1f);
        StripCollider(go);
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(floorColor);
    }

    void BuildHair()
    {
        _hairRoot = new GameObject("FallingHair").transform;
        _hairRoot.SetParent(transform, false);

        int n = Mathf.Max(4, nodes);
        _p = new Vector3[n];
        _prev = new Vector3[n];
        _spacing = hairLength / (n - 1);

        var mat = MakeMaterial(hairColor);
        _segments = new Transform[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Seg" + i;
            go.transform.SetParent(_hairRoot, false);
            StripCollider(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            _segments[i] = go.transform;
        }
    }

    // ------------------------------------------------------------------
    // 縦画面
    // ------------------------------------------------------------------

    void ApplyPortraitRect()
    {
        if (_cam == null) return;
        _cam.orthographicSize = viewHalfHeight;

        float screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        if (screenAspect > portraitAspect)
        {
            float w = portraitAspect / screenAspect;
            _cam.rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }
        else
        {
            float h = screenAspect / portraitAspect;
            _cam.rect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
        }
        if (_letterboxCam != null) _letterboxCam.rect = new Rect(0f, 0f, 1f, 1f);
    }

    // ------------------------------------------------------------------
    // ヘルパー
    // ------------------------------------------------------------------

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
}
