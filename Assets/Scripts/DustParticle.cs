using UnityEngine;

public class DustParticle : MonoBehaviour
{
    public enum Kind { Dust, Pollen }

    Vector3 _center;
    float   _radius;
    Vector3 _dir;
    float   _driftDeg;
    Vector3 _driftAxis;
    float   _bobPhase;
    float   _bobAmp;
    bool    _stuck;
    int     _checkTimer;

    public static float StickDistance = 0.04f;

    public void Init(Vector3 center, float radius, Vector3 startDir,
                     float driftDeg, Vector3 driftAxis, float bobAmp)
    {
        _center    = center;
        _radius    = radius;
        _dir       = startDir.normalized;
        _driftDeg  = driftDeg;
        _driftAxis = driftAxis;
        _bobPhase  = Random.Range(0f, Mathf.PI * 2f);
        _bobAmp    = bobAmp;
        _checkTimer = Random.Range(0, 5);
        PlaceAt(_radius);
    }

    void Update()
    {
        if (_stuck) return;

        float dt = Time.deltaTime;
        _dir = (Quaternion.AngleAxis(_driftDeg * dt, _driftAxis) * _dir).normalized;
        PlaceAt(_radius + Mathf.Sin(Time.time * 0.7f + _bobPhase) * _bobAmp);

        // 5フレームに1回、毛との距離チェック
        if (++_checkTimer < 5) return;
        _checkTimer = 0;

        var hairs = PlanetHair.All;
        for (int i = 0; i < hairs.Count; i++)
        {
            var h = hairs[i];
            if (h == null) continue;
            if (Vector3.Distance(transform.position, h.transform.position) < StickDistance)
            {
                StickToHair(h);
                return;
            }
        }
    }

    void PlaceAt(float r)
    {
        transform.position = _center + _dir * r;
    }

    void StickToHair(PlanetHair hair)
    {
        _stuck = true;
        // worldPositionStays=false でローカル座標系に入り、ストランド上に刺さった位置へ
        transform.SetParent(hair.transform, false);
        transform.localPosition = new Vector3(
            Random.Range(-0.002f, 0.002f),   // 横方向（毛の太さ内）
            Random.Range(-0.002f, 0.002f),
            Random.Range(-0.025f, 0.025f)    // ストランドの長さ方向
        );
        transform.localRotation = Quaternion.Euler(
            Random.Range(-40f, 40f), Random.Range(0f, 360f), 0f);
    }

    // ---- スポーン ----

    public static DustParticle Spawn(Transform parent, Vector3 center,
                                     float radius, Kind kind, Material mat)
    {
        var go = new GameObject(kind == Kind.Pollen ? "Pollen" : "Dust");
        go.transform.SetParent(parent, false);

        // 見た目: 花粉=黄色い球, チリ=小さい灰色の球
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(go.transform, false);
        float size = kind == Kind.Pollen
            ? Random.Range(0.012f, 0.020f)
            : Random.Range(0.005f, 0.010f);
        sphere.transform.localScale = Vector3.one * size;
        sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
        var col = sphere.GetComponent<Collider>();
        if (col) Object.Destroy(col);

        var dp         = go.AddComponent<DustParticle>();
        float driftDeg = Random.Range(4f, 18f) * (Random.value < 0.5f ? 1f : -1f);
        dp.Init(center, radius, Random.onUnitSphere, driftDeg,
                Random.onUnitSphere, Random.Range(0.01f, 0.05f));
        return dp;
    }
}
