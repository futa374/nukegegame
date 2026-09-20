using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// proto1 用の視点。左クリックは静電気ラインの点打ちに使うので、視点回転は右ドラッグへ譲る。
/// 右ボタンを動かさずに離したときは、線の確定として StaticLineField へ渡す。
/// </summary>
public class ProtoCameraRig : MonoBehaviour
{
    public Vector3 pivot = Vector3.zero;
    public float distance = 8f;
    public float minDistance = 2.6f;
    public float maxDistance = 20f;
    public float azimuth = 200f;
    public float elevation = 14f;
    public float rotateSpeed = 0.22f;
    [Tooltip("ホイール1ノッチあたりの寄り具合。0.12でだいたい12%ずつ。")]
    public float zoomSpeed = 0.12f;

    [Tooltip("これ以下の移動量なら、右クリックは回転ではなく線の確定と見なす。")]
    public float clickThreshold = 6f;

    float _rightDragDistance;
    bool  _startedWithModifier;

    void Start() { Apply(); }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // Cmd / Ctrl を押しながらの右クリックは「もやの点を置く」なので、視点は動かさない。
        bool mod = StaticLineField.ModifierHeld();

        if (mouse.rightButton.wasPressedThisFrame) { _rightDragDistance = 0f; _startedWithModifier = mod; }

        if (mouse.rightButton.isPressed && !_startedWithModifier)
        {
            Vector2 d = mouse.delta.ReadValue();
            _rightDragDistance += d.magnitude;
            azimuth   -= d.x * rotateSpeed;
            elevation = Mathf.Clamp(elevation + d.y * rotateSpeed, -85f, 85f);
        }

        // 修飾キー無しで、動かさずに離したとき: そこへもやの点を置く。
        // Cmd が届かない環境（macOS のエディタなど）でも、これだけで引ける。
        // 引き終わりは Enter か Esc。
        if (mouse.rightButton.wasReleasedThisFrame && !_startedWithModifier && _rightDragDistance < clickThreshold)
        {
            var field = StaticLineField.Instance;
            if (field != null) field.PlaceNodeAtScreen(mouse.position.ReadValue());
        }

        // ホイールの1ノッチは環境によって 1 だったり 120 だったりする。
        // 生の値を距離に掛けると一気に寄りすぎるので、ノッチ数に正規化してから指数で効かせる。
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float notches = Mathf.Clamp(scroll / (Mathf.Abs(scroll) > 10f ? 120f : 1f), -3f, 3f);
            distance = Mathf.Clamp(distance * Mathf.Exp(-notches * zoomSpeed), minDistance, maxDistance);
        }

        Apply();
    }

    void Apply()
    {
        Quaternion rot = Quaternion.Euler(elevation, azimuth, 0f);
        transform.position = pivot + rot * (Vector3.back * distance);
        transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
    }
}
