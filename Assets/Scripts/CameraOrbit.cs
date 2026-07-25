using UnityEngine;
using UnityEngine.InputSystem;

public class CameraOrbit : MonoBehaviour
{
    public Transform target;
    public float sensitivityH = 0.1f;
    public float sensitivityV = 0.1f;
    public float zoomSensitivity = 0.01f;
    public float distance = 3f;
    public float maxDistance = 10f;
    public float verticalOffset = 0f;

    [Header("Scene Transition")]
    public float transitionDistance = 2.5f;
    public string transitionScene = "ScalpScene";
    public string backTransitionScene = "";

    private bool transitioning = false;

    public float initialAzimuth = 0f;
    public float initialElevation = 0f;

    private float azimuth;
    private float elevation;
    private Vector3 pivotPoint;

    void Start()
    {
        if (target == null)
        {
            var go = GameObject.Find("face");
            if (go != null) target = go.transform;
        }

        azimuth = initialAzimuth;
        elevation = initialElevation;

        if (SceneTransitioner.Instance != null && SceneTransitioner.Instance.savedDistance > 0f)
        {
            distance  = SceneTransitioner.Instance.savedDistance;
            azimuth   = SceneTransitioner.Instance.savedAzimuth;
            elevation = SceneTransitioner.Instance.savedElevation;
            SceneTransitioner.Instance.savedDistance = -1f;
        }

        UpdatePivot();
    }

    public bool useBoundsCenter = true;

    void UpdatePivot()
    {
        if (target == null) return;

        if (useBoundsCenter)
        {
            var r = target.GetComponentInChildren<Renderer>();
            pivotPoint = r != null ? r.bounds.center : target.position;
        }
        else
        {
            pivotPoint = target.position;
        }
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
            distance = Mathf.Clamp(distance, transitionDistance, maxDistance);

            if (!transitioning && !string.IsNullOrEmpty(transitionScene) && distance <= transitionDistance)
            {
                transitioning = true;
                var t = SceneTransitioner.Get();
                t.savedDistance  = distance;
                t.savedAzimuth   = azimuth;
                t.savedElevation = elevation;
                t.TransitionTo(transitionScene);
            }
            if (!transitioning && !string.IsNullOrEmpty(backTransitionScene) && distance >= maxDistance)
            {
                transitioning = true;
                SceneTransitioner.Get().TransitionTo(backTransitionScene);
            }
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

        Debug.Log($"dist={distance:F2} az={azimuth:F1} el={elevation:F1} pivot={pivotPoint}");
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
