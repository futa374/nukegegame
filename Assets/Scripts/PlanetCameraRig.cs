using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// planet シーン用のシンプルな周回カメラ。
/// 左ドラッグで回転、スクロール（二本指）でズーム。地球の中心 pivot を見続ける。
/// PlanetController がカメラに付けて設定する。既存スクリプトには手を加えない。
/// </summary>
public class PlanetCameraRig : MonoBehaviour
{
    public Vector3 pivot;
    public float distance = 8.5f;
    public float minDistance = 3.5f;
    public float maxDistance = 18f;
    public float azimuth = 180f;
    public float elevation = 8f;
    public float rotateSpeed = 0.25f;
    public float zoomSpeed = 0.02f;
    public float minElevation = -85f;
    public float maxElevation = 85f;

    void Start() { Apply(); }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                azimuth   += d.x * rotateSpeed;
                elevation -= d.y * rotateSpeed;
                elevation = Mathf.Clamp(elevation, minElevation, maxElevation);
            }
            float sy = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(sy) > 0.001f)
            {
                distance -= sy * zoomSpeed;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }
        }
        Apply();
    }

    void Apply()
    {
        float az = azimuth * Mathf.Deg2Rad, el = elevation * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        transform.position = pivot + dir * distance;
        transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
    }
}
