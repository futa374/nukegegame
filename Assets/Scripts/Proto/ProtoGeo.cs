using UnityEngine;

/// <summary>
/// 着地点を地球上の座標として読む。
///
/// 星は自転しているので、着地した瞬間のワールド座標のままでは「どこに落ちたか」が言えない。
/// 球のローカル空間へ戻してから緯度経度を出す。
///
/// 経度をテクスチャのどこに合わせるかは、球メッシュの UV から逆算している。
/// Unity の球プリミティブの UV 規約を決め打ちすると、地図が半周ずれたり左右反転したりする。
/// 頂点座標と UV の対応を実際に読んで合わせれば、規約が何であれ正しい経度が出る。
/// </summary>
public static class ProtoGeo
{
    static bool  _calibrated;
    static float _lonSign = 1f;
    static float _lonOffset;

    /// <summary>球メッシュの頂点と UV から、経度とテクスチャの対応を求める。</summary>
    public static void Calibrate(Mesh sphere)
    {
        if (_calibrated || sphere == null) return;
        var v = sphere.vertices;
        var uv = sphere.uv;
        if (v.Length == 0 || uv.Length != v.Length) { _calibrated = true; return; }

        // 極の近くは経度が定まらないので、赤道帯の頂点だけ使う。
        float bestVar = float.MaxValue;
        foreach (float sign in new[] { 1f, -1f })
        {
            double sx = 0, sy = 0; int n = 0;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i].normalized;
                if (Mathf.Abs(p.y) > 0.5f) continue;
                float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg * sign;   // -180..180
                float lon = (uv[i].x - 0.5f) * 360f;                      // 等距円筒図法として読む
                float d = Mathf.DeltaAngle(a, lon);
                sx += Mathf.Cos(d * Mathf.Deg2Rad); sy += Mathf.Sin(d * Mathf.Deg2Rad);
                n++;
            }
            if (n == 0) continue;
            float mean = Mathf.Atan2((float)(sy / n), (float)(sx / n)) * Mathf.Rad2Deg;
            float r = Mathf.Sqrt((float)(sx * sx + sy * sy)) / n;          // 1に近いほどばらつきが小さい
            float variance = 1f - r;
            if (variance < bestVar) { bestVar = variance; _lonSign = sign; _lonOffset = mean; }
        }
        _calibrated = true;
    }

    /// <summary>球のローカル座標から緯度・経度（度）を求める。</summary>
    public static void LatLon(Vector3 local, out float lat, out float lon)
    {
        Vector3 p = local.normalized;
        lat = Mathf.Asin(Mathf.Clamp(p.y, -1f, 1f)) * Mathf.Rad2Deg;
        float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg * _lonSign;
        lon = Mathf.DeltaAngle(0f, a + _lonOffset);
    }

    /// <summary>緯度・経度から球のローカル方向ベクトルへ。LatLon の逆。</summary>
    public static Vector3 Direction(float lat, float lon)
    {
        float a = Mathf.DeltaAngle(0f, (lon - _lonOffset)) * _lonSign;   // atan2(x,z) の角度へ戻す
        float ar = a * Mathf.Deg2Rad, lr = lat * Mathf.Deg2Rad;
        float c = Mathf.Cos(lr);
        return new Vector3(Mathf.Sin(ar) * c, Mathf.Sin(lr), Mathf.Cos(ar) * c);
    }

    public static string Format(float lat, float lon)
    {
        string ns = lat >= 0f ? "北緯" : "南緯";
        string ew = lon >= 0f ? "東経" : "西経";
        return $"{ns} {Mathf.Abs(lat):F1}°  {ew} {Mathf.Abs(lon):F1}°";
    }

    // 粗い区画。厳密な国境ではなく、レポートに一言添えるための目安。
    struct Box { public float latMin, latMax, lonMin, lonMax; public string name;
        public Box(float a, float b, float c, float d, string n){latMin=a;latMax=b;lonMin=c;lonMax=d;name=n;} }

    static readonly Box[] _regions = {
        new Box( 66,  90, -180, 180, "北極海"),
        new Box(-90, -60, -180, 180, "南極大陸"),
        new Box(-60, -50, -180, 180, "南極海"),

        new Box( 10,  72,  -12,  60, "ユーラシア西部"),
        new Box( 20,  55,   60, 145, "ユーラシア東部"),
        new Box(  5,  30,   60,  95, "インド亜大陸"),
        new Box(-11,  22,   95, 142, "東南アジア"),
        new Box(-35,  37,  -18,  52, "アフリカ大陸"),
        new Box( 15,  70, -168, -55, "北アメリカ大陸"),
        new Box(-56,  13,  -82, -34, "南アメリカ大陸"),
        new Box(-44, -10,  113, 154, "オーストラリア大陸"),

        new Box(-60,  66,  -80, -10, "大西洋"),
        new Box(-60,  30,   20, 115, "インド洋"),
        new Box(-60,  66,  145, 180, "太平洋"),
        new Box(-60,  66, -180, -85, "太平洋"),
    };

    /// <summary>緯度経度から、おおよその地名を返す。当てはまらなければ海。</summary>
    public static string Region(float lat, float lon)
    {
        foreach (var b in _regions)
            if (lat >= b.latMin && lat <= b.latMax && lon >= b.lonMin && lon <= b.lonMax)
                return b.name;
        return "外洋";
    }
}
