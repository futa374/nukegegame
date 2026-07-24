using UnityEngine;
using UnityEngine.InputSystem;

public class CameraOrbit : MonoBehaviour
{
    public Transform target;
    public float sensitivityH = 0.1f;
    public float sensitivityV = 0.1f;
    public float distance = 3f;
    public float verticalOffset = 0f;

    private float azimuth = 0f;
    private float elevation = 0f;

    void Start()
    {
        if (target == null)
        {
            var go = GameObject.Find("face");
            if (go != null) target = go.transform;
        }
    }

    void Update()
    {
        if (target == null) return;

        Vector2 scroll = Mouse.current.scroll.ReadValue();
        azimuth   += scroll.x * sensitivityH;
        elevation += scroll.y * sensitivityV;
        elevation = Mathf.Clamp(elevation, 0f, 80f);

        float azRad  = azimuth  * Mathf.Deg2Rad;
        float elRad  = elevation * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Cos(elRad) * Mathf.Sin(azRad),
            Mathf.Sin(elRad),
            Mathf.Cos(elRad) * Mathf.Cos(azRad)
        ) * distance;

        Vector3 pivot = target.position + Vector3.up * verticalOffset;
        transform.position = pivot + offset;
        transform.LookAt(pivot);
    }
}
