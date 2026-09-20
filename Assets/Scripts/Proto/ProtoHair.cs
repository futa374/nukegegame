using UnityEngine;

/// <summary>
/// proto1「抜け毛テラフォーミング」の落下毛。
///
/// 既存の PlanetHair は、中心からの半径を縮めながら向きを回すスクリプト制御で落ちている。
/// 決められた道筋を辿るだけなので、外から弾かれて浮き上がる、勢いが残って行き過ぎる、
/// という出来事が起こらない。静電気ラインで軌道を変える体験は、そこが無いと成立しない。
///
/// なのでこの毛は速度を持つ。重力と空気抵抗、それに静電気ラインからの力を毎フレーム積分する。
/// 毛は軽くて面積が大きいので、空気抵抗が効いてすぐ終端速度に落ち着く。落ちるというより
/// 沈んでいく。その遅さが、線を引いて掬い上げる時間を作る。
/// </summary>
public class ProtoHair : MonoBehaviour
{
    // --- 場の設定（生成側が渡す） ---
    Vector3 _center;
    float   _landRadius;
    float   _gravity;
    float   _drag;

    // --- 運動状態 ---
    Vector3 _vel;
    bool    _landed;
    float   _spin;
    float   _spinSpeed;

    // --- 記録（旅路レポートの素） ---
    // 部屋の床では、一本の毛がどこを通ってきたかを追う手立てがない。
    // 落ちた結果だけが残る。ここでは通ってきた道と、途中で起きたことを控えておく。
    public string  ownerName = "";
    public float   birthTime;
    public float   airTime;          // 滞空していた秒数
    public int     caughtCount;      // もやに捕まった回数
    public float   travelDistance;   // 移動した総距離
    public float   maxAltitude;      // 地表からの最高到達高度
    public float   peakSpeed;        // 最高速度
    public int     contactCount;     // 他の毛とすれ違った回数
    bool _wasCaught;
    bool _caught;
    public bool IsCaught => _caught;

    /// <summary>
    /// 星の本体。着地したらこの子になって、一緒に回る。
    /// 軌跡もこの座標系で控える。星が自転している以上、「どこを通ったか」は
    /// 宇宙に対してではなく、地面に対して言うほうが景観の話として筋が通る。
    /// </summary>
    public Transform globe;

    [Tooltip("軌跡を何秒おきに記録するか。")]
    public float trailSampleInterval = 0.25f;
    // 球のローカル座標で持つ。ワールドで持つと、星が回ったぶんだけ線が毛から離れていく。
    readonly System.Collections.Generic.List<Vector3> _trail = new System.Collections.Generic.List<Vector3>();
    public System.Collections.Generic.IReadOnlyList<Vector3> Trail => _trail;
    float _trailTimer;

    Vector3 ToLocal(Vector3 world) => globe != null ? globe.InverseTransformPoint(world) : world;

    /// <summary>旅の途中で起きたこと。時刻（離脱からの秒）と内容。</summary>
    public readonly System.Collections.Generic.List<(float t, string what)> events
        = new System.Collections.Generic.List<(float, string)>();
    int _contactTimer;
    readonly System.Collections.Generic.HashSet<ProtoHair> _metBefore
        = new System.Collections.Generic.HashSet<ProtoHair>();

    static readonly System.Collections.Generic.List<ProtoHair> _all = new System.Collections.Generic.List<ProtoHair>();
    public static System.Collections.Generic.IReadOnlyList<ProtoHair> All => _all;

    void OnEnable()  { _all.Add(this); }
    void OnDisable() { _all.Remove(this); }

    public bool IsLanded => _landed;

    public void Init(Vector3 center, float landRadius, Vector3 startPos, Vector3 startVel,
                     Material mat, float thickness, float length,
                     float gravity, float drag)
    {
        _center     = center;
        _landRadius = landRadius;
        _gravity    = gravity;
        _drag       = drag;
        _vel        = startVel;
        _spin       = Random.Range(0f, 360f);
        _spinSpeed  = Random.Range(-40f, 40f);
        birthTime   = Time.time;

        transform.position = startPos;
        strandLength = length; strandThickness = thickness;
        _trail.Add(ToLocal(startPos));
        events.Add((0f, "頭皮を離れる"));
        PlanetHair.BuildStrand(gameObject, length, thickness, mat);
        Orient();
    }

    void Update()
    {
        if (_landed) return;

        float dt = Time.deltaTime;
        Vector3 pos = transform.position;
        Vector3 up  = (pos - _center).normalized;

        // 重力は常に星の中心へ。
        Vector3 acc = -up * _gravity;

        // 静電気のもやからの力。捕まっている間は重力を打ち消し、帯の中をゆっくり漂う。
        var field = StaticLineField.Instance;
        bool caught = false;
        if (field != null)
        {
            acc += field.ForceOn(pos, _vel, out caught);
            // もやの中に留まっている毛どうしは、弱く寄り集まって房になる。
            // 一本ずつ散らばって浮いているより、塊として溜まっていくほうが、
            // どれだけ受け止めたかが見てわかる。
            if (caught) acc += Cohesion(field, pos);
        }

        if (caught && !_wasCaught) { caughtCount++; events.Add((airTime, "静電気のもやに捕まる")); }
        if (!caught && _wasCaught)  events.Add((airTime, "もやを抜けて落下を再開"));
        _wasCaught = caught;
        _caught = caught;

        _vel += acc * dt;

        // 空気抵抗。毛は軽く面積が大きいので、速度に比例した抵抗がよく効く。
        _vel *= Mathf.Exp(-_drag * dt);

        Vector3 next = pos + _vel * dt;
        travelDistance += (next - pos).magnitude;
        airTime += dt;
        transform.position = next;

        // 通ってきた道を控える。着地後、金色の線として引き直せるように。
        float alt = (next - _center).magnitude - _landRadius;
        if (alt > maxAltitude) maxAltitude = alt;
        float sp = _vel.magnitude;
        if (sp > peakSpeed) peakSpeed = sp;
        _trailTimer += dt;
        if (_trailTimer >= trailSampleInterval)
        {
            _trailTimer = 0f;
            _trail.Add(ToLocal(next));
        }

        // 他の毛とのすれ違い。衝突として弾ませはしないが、出会ったことは記録する。
        if (++_contactTimer >= 12)
        {
            _contactTimer = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                var o = _all[i];
                if (o == null || o == this || o._landed || _metBefore.Contains(o)) continue;
                if (Vector3.Distance(next, o.transform.position) < 0.06f)
                {
                    _metBefore.Add(o);
                    contactCount++;
                    events.Add((airTime, $"{o.ownerName} の毛とすれ違う"));
                    if (contactCount >= 6) break;
                }
            }
        }

        _spin += _spinSpeed * dt;
        Orient();

        if ((next - _center).magnitude <= _landRadius) Land();
    }

    /// <summary>
    /// もやの中で捕まっている毛どうしの弱い引力。房をつくる。
    /// 近づきすぎたら押し返して、一点に潰れないようにする。
    /// </summary>
    Vector3 Cohesion(StaticLineField field, Vector3 pos)
    {
        float r = field.cohesionRadius;
        float rMin = field.cohesionMinDistance;
        Vector3 sum = Vector3.zero;
        int n = 0;
        var all = _all;
        for (int i = 0; i < all.Count; i++)
        {
            var o = all[i];
            if (o == null || o == this || o._landed || !o._caught) continue;
            Vector3 d = o.transform.position - pos;
            float dist = d.magnitude;
            if (dist > r || dist < 1e-5f) continue;
            if (dist < rMin) sum -= d.normalized * (1f - dist / rMin);   // 近すぎたら離れる
            else             sum += d.normalized * (1f - dist / r);
            n++;
        }
        if (n == 0) return Vector3.zero;
        return sum / n * field.cohesionStrength;
    }

    /// <summary>毛先を進行方向へ向ける。止まっているときは地面に沿って寝かせる。</summary>
    void Orient()
    {
        Vector3 up = (transform.position - _center).normalized;
        Vector3 fwd = _vel.sqrMagnitude > 1e-6f ? _vel.normalized : Vector3.Cross(up, Vector3.right);
        // 進行方向が真上真下だと LookRotation が破綻するので、接線へ倒す。
        if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.98f)
        {
            fwd = Vector3.Cross(up, Vector3.right);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.Cross(up, Vector3.forward);
        }
        fwd.Normalize();
        transform.rotation = Quaternion.LookRotation(fwd, up) * Quaternion.AngleAxis(_spin, Vector3.forward);
    }

    /// <summary>この毛のレポート番号。着地時に旅路レポート側から入る。</summary>
    public int reportIndex = -1;

    [System.NonSerialized] public float strandLength, strandThickness;

    void Land()
    {
        _landed = true;
        _caught = false;
        _vel = Vector3.zero;
        Vector3 up = (transform.position - _center).normalized;
        transform.position = _center + up * _landRadius;
        _trail.Add(ToLocal(transform.position));
        events.Add((airTime, "着地"));

        // 地面に着いたのだから、以後は星と一緒に回る。
        // ワールドに置き去りにすると、星だけが下を滑っていって、毛が宙に浮いて見える。
        if (globe != null) transform.SetParent(globe, true);

        // 地面に寝かせる。接線方向へ倒し、わずかに向きを散らす。
        Vector3 tangent = Vector3.Cross(up, Random.onUnitSphere);
        if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.Cross(up, Vector3.forward);
        transform.rotation = Quaternion.LookRotation(tangent.normalized, up);

        // 着地してはじめて、選んで読める対象になる。
        // 落下中は掴めない——それが観察という立場だという気もする。
        var col = gameObject.AddComponent<CapsuleCollider>();
        col.direction = 2;                                     // ストランドのローカルZ軸
        col.height = strandLength * 1.4f;
        col.radius = Mathf.Max(strandThickness * 6f, 0.02f);   // 細すぎると拾えないので太めに

        var log = ProtoJourneyLog.Instance;
        if (log != null) log.Record(this);
    }
}
