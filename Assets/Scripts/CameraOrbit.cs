using UnityEngine;
using UnityEngine.InputSystem;

public class CameraOrbit : MonoBehaviour
{
    public Transform target;
    public float sensitivityH = 0.1f;
    public float sensitivityV = 0.1f;
    public float zoomSensitivity = 0.01f;
    public float distance = 3f;
    public float minDistance = 0.5f;
    public float maxDistance = 10f;
    public float verticalOffset = 0f;

    private float azimuth = 0f;
    private float elevation = 0f;
    private Vector3 pivotPoint;

    void Start()
    {
        if (target == null)
        {
            var go = GameObject.Find("face");
            if (go != null) target = go.transform;
        }

        UpdatePivot();
    }

    void UpdatePivot()
    {
        if (target == null) return;

        var renderer = target.GetComponentInChildren<Renderer>();
        pivotPoint = renderer != null ? renderer.bounds.center : target.position;
        pivotPoint += Vector3.up * verticalOffset;
    }

    void Update()
    {
        if (target == null) return;

        Vector2 scroll = Mouse.current.scroll.ReadValue();
        bool ctrl = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;

        if (ctrl)
        {
            distance -= scroll.y * zoomSensitivity;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }
        else
        {
            azimuth   += scroll.x * sensitivityH;
            elevation += scroll.y * sensitivityV;
            elevation = Mathf.Clamp(elevation, 0f, 80f);
        }

        float azRad = azimuth  * Mathf.Deg2Rad;
        float elRad = elevation * Mathf.Deg2Rad;

        Vector3 offset = new Vector3(
            Mathf.Cos(elRad) * Mathf.Sin(azRad),
            Mathf.Sin(elRad),
            Mathf.Cos(elRad) * Mathf.Cos(azRad)
        ) * distance;

        transform.position = pivotPoint + offset;
        transform.LookAt(pivotPoint);
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 18;
        style.normal.textColor = Color.white;

        float x = 20f;
        float y = Screen.height - 110f;
        float w = 400f;
        float h = 28f;

        GUI.Label(new Rect(x, y,      w, h), "二本指 左右スクロール　：　水平回転", style);
        GUI.Label(new Rect(x, y + 30, w, h), "二本指 上下スクロール　：　垂直回転", style);
        GUI.Label(new Rect(x, y + 60, w, h), "Ctrl + 上下スクロール　：　ズーム",   style);
    }
}
