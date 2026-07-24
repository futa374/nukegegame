using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 左ドラッグでカメラを水平にパン、スクロール／ピンチでズームする見下ろしカメラ操作。
/// パンはカーソル下の点が吸い付く 1:1 方式。任意のカメラ角度でも動作する。
/// タイトル画面中（TitleScreen.Active==false）は動かない。
/// </summary>
[RequireComponent(typeof(Camera))]
public class DragPanCamera : MonoBehaviour
{
    [Header("パン")]
    [Tooltip("パンの基準になる水平面の高さ（部屋の床の Y 座標）。ここを掴んで動かす。")]
    public float groundY = 0f;

    [Header("ズーム")]
    [Tooltip("ズームの感度。強すぎ／弱すぎたらここを調整。")]
    public float zoomSpeed = 2f;
    [Tooltip("寄れる限界（カメラの最小の高さ / 最小 orthographicSize）。")]
    public float minZoom = 3f;
    [Tooltip("引ける限界（カメラの最大の高さ / 最大 orthographicSize）。")]
    public float maxZoom = 40f;

    Camera _cam;
    Vector3 _grabWorldPoint;
    bool _dragging;

    void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    void Update()
    {
        if (!TitleScreen.Active) { _dragging = false; return; }

        HandlePan();
        HandleZoom();
    }

    void HandlePan()
    {
        if (LeftPressedThisFrame())
        {
            if (RaycastGround(MousePosition(), out _grabWorldPoint))
                _dragging = true;
        }

        if (_dragging && LeftHeld())
        {
            if (RaycastGround(MousePosition(), out Vector3 current))
            {
                Vector3 delta = _grabWorldPoint - current;
                delta.y = 0f;
                transform.position += delta;
            }
        }
        else
        {
            _dragging = false;
        }
    }

    void HandleZoom()
    {
        float scroll = ScrollDelta();
        if (Mathf.Abs(scroll) < 0.0001f) return;

        if (_cam.orthographic)
        {
            float size = _cam.orthographicSize - scroll * zoomSpeed;
            _cam.orthographicSize = Mathf.Clamp(size, minZoom, maxZoom);
        }
        else
        {
            Vector3 pos = transform.position + transform.forward * (scroll * zoomSpeed);
            float height = pos.y - groundY;
            if (height < minZoom || height > maxZoom) return;
            transform.position = pos;
        }
    }

    bool RaycastGround(Vector2 screenPos, out Vector3 world)
    {
        Plane plane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
        Ray ray = _cam.ScreenPointToRay(screenPos);
        if (plane.Raycast(ray, out float enter))
        {
            world = ray.GetPoint(enter);
            return true;
        }
        world = Vector3.zero;
        return false;
    }

    bool LeftPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    bool LeftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }

    Vector2 MousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return (Vector2)Input.mousePosition;
#endif
    }

    float ScrollDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
        return Input.mouseScrollDelta.y;
#endif
    }
}
