using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 既存の PlanetHair を、速度を持つ落下へ差し替える。
///
/// PlanetHair は半径を縮めながら向きを回すスクリプト制御で落ちる。決められた道筋を
/// 辿るだけなので、外から弾かれて浮き上がる、勢いが残って行き過ぎる、が起こらない。
/// 静電気のもやで軌道を変える体験は、そこが無いと成立しない。
///
/// そこで PlanetHair の落下だけを止め（drivenExternally）、この成分が位置を持つ。
/// 毛の見た目・輪郭表示・絡まり判定・着地後の蓄積は、既存の仕組みをそのまま使う。
/// 新しいシーンに足すだけで済み、これまでのシーンには影響しない。
/// </summary>
[RequireComponent(typeof(PlanetHair))]
public class FieldHair : MonoBehaviour
{
    PlanetHair _hair;
    Transform  _globe;
    Vector3 _center;
    float   _landRadius;
    float   _gravity, _drag;
    Vector3 _vel;
    bool    _done;

    // 旅路レポートの素
    public string ownerName = "";
    public float  airTime, travelDistance, maxAltitude, peakSpeed;
    public int    caughtCount, contactCount;
    bool _wasCaught, _caught;
    public bool IsCaught => _caught;

    float _trailTimer, _sampleInterval = 0.25f;
    readonly List<Vector3> _trail = new List<Vector3>();     // 球のローカル座標
    public IReadOnlyList<Vector3> Trail => _trail;
    public readonly List<(float t, string what)> events = new List<(float, string)>();
    int _contactTimer;
    readonly HashSet<FieldHair> _met = new HashSet<FieldHair>();

    static readonly List<FieldHair> _all = new List<FieldHair>();
    public static IReadOnlyList<FieldHair> All => _all;
    void OnEnable()  { _all.Add(this); }
    void OnDisable() { _all.Remove(this); }

    public void Init(Transform globe, Vector3 center, float landRadius,
                     Vector3 startVel, float gravity, float drag, string owner)
    {
        _hair = GetComponent<PlanetHair>();
        _hair.drivenExternally = true;
        _globe = globe; _center = center; _landRadius = landRadius;
        _gravity = gravity; _drag = drag; _vel = startVel;
        ownerName = string.IsNullOrEmpty(owner) ? _hair.ownerName : owner;
        _trail.Add(ToLocal(transform.position));
        events.Add((0f, "頭皮を離れる"));
    }

    Vector3 ToLocal(Vector3 w) => _globe != null ? _globe.InverseTransformPoint(w) : w;

    void Update()
    {
        if (_done) return;

        float dt = Time.deltaTime;
        Vector3 pos = transform.position;
        Vector3 up = (pos - _center).normalized;
        Vector3 acc = -up * _gravity;

        var field = StaticLineField.Instance;
        bool caught = false;
        if (field != null)
        {
            acc += field.ForceOn(pos, _vel, out caught);
            if (caught) acc += Cohesion(field, pos);
        }
        if (caught && !_wasCaught) { caughtCount++; events.Add((airTime, "静電気のもやに捕まる")); }
        if (!caught && _wasCaught)  events.Add((airTime, "もやを抜けて落下を再開"));
        _wasCaught = caught; _caught = caught;

        _vel += acc * dt;
        _vel *= Mathf.Exp(-_drag * dt);

        Vector3 next = pos + _vel * dt;
        travelDistance += (next - pos).magnitude;
        airTime += dt;
        transform.position = next;

        float alt = (next - _center).magnitude - _landRadius;
        if (alt > maxAltitude) maxAltitude = alt;
        float sp = _vel.magnitude;
        if (sp > peakSpeed) peakSpeed = sp;

        _trailTimer += dt;
        if (_trailTimer >= _sampleInterval) { _trailTimer = 0f; _trail.Add(ToLocal(next)); }

        if (++_contactTimer >= 12)
        {
            _contactTimer = 0;
            for (int i = 0; i < _all.Count && contactCount < 6; i++)
            {
                var o = _all[i];
                if (o == null || o == this || o._done || _met.Contains(o)) continue;
                if (Vector3.Distance(next, o.transform.position) < 0.06f)
                {
                    _met.Add(o); contactCount++;
                    events.Add((airTime, $"{o.ownerName} の毛とすれ違う"));
                }
            }
        }

        Orient(next);

        if ((next - _center).magnitude <= _landRadius) Land();
    }

    Vector3 Cohesion(StaticLineField field, Vector3 pos)
    {
        float r = field.cohesionRadius, rMin = field.cohesionMinDistance;
        Vector3 sum = Vector3.zero; int n = 0;
        for (int i = 0; i < _all.Count; i++)
        {
            var o = _all[i];
            if (o == null || o == this || o._done || !o._caught) continue;
            Vector3 d = o.transform.position - pos;
            float dist = d.magnitude;
            if (dist > r || dist < 1e-5f) continue;
            if (dist < rMin) sum -= d.normalized * (1f - dist / rMin);
            else             sum += d.normalized * (1f - dist / r);
            n++;
        }
        return n == 0 ? Vector3.zero : sum / n * field.cohesionStrength;
    }

    void Orient(Vector3 pos)
    {
        Vector3 up = (pos - _center).normalized;
        Vector3 fwd = _vel.sqrMagnitude > 1e-6f ? _vel.normalized : Vector3.Cross(up, Vector3.right);
        if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.98f)
        {
            fwd = Vector3.Cross(up, Vector3.right);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.Cross(up, Vector3.forward);
        }
        transform.rotation = Quaternion.LookRotation(fwd.normalized, up);
    }

    /// <summary>
    /// 供給が上限に達したときなど、外から着地させる。
    /// 毛は決して消えない。消えると地表の蓄積が止まり、この作品が成り立たない。
    /// </summary>
    public void ForceLand() { if (!_done) Land(); }

    void Land()
    {
        _done = true; _caught = false;
        Vector3 up = (transform.position - _center).normalized;
        transform.position = _center + up * _landRadius;
        Vector3 tangent = Vector3.Cross(up, Random.onUnitSphere);
        if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.Cross(up, Vector3.forward);
        transform.rotation = Quaternion.LookRotation(tangent.normalized, up);

        _trail.Add(ToLocal(transform.position));
        events.Add((airTime, "着地"));

        var log = ProtoJourneyLog.Instance;
        if (log != null) log.Record(this, _globe);

        // 地表の被覆を数える側へ、着地を知らせる
        var ending = ProtoEnding.Instance;
        if (ending != null) ending.RegisterLanding(transform.position);

        // 着地後は既存の蓄積システムに任せる。個別オブジェクトは破棄され、
        // 地球ローカルの姿だけが残って、自転と一緒に回る。
        _hair.drivenExternally = false;
        _hair.SettleNow();
    }
}
