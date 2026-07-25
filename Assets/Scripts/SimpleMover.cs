using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// オブジェクトを平行移動させる（スライド移動）。有効化された時点の位置を基準に往復する。
/// </summary>
public class SimpleMover : MonoBehaviour
{
    public enum Mode { AutoPingPong, Keyboard }
    public Mode mode = Mode.AutoPingPong;
    public float speed = 1.2f;
    public Vector3 direction = Vector3.right;
    public float range = 2.5f;

    Vector3 _start;
    float _t;

    void OnEnable() { _start = transform.position; _t = 0f; }

    void Update()
    {
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
        if (mode == Mode.AutoPingPong)
        {
            _t += Time.deltaTime;
            float w = speed / Mathf.Max(range, 0.01f);
            transform.position = _start + dir * (Mathf.Sin(_t * w) * range);
        }
        else
        {
            transform.position += dir * (ReadHorizontal() * speed * Time.deltaTime);
        }
    }

    float ReadHorizontal()
    {
#if ENABLE_INPUT_SYSTEM
        float v = 0f; var kb = Keyboard.current;
        if (kb != null) { if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) v -= 1f; if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) v += 1f; }
        return v;
#else
        return Input.GetAxisRaw("Horizontal");
#endif
    }
}
