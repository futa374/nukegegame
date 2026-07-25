using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// オブジェクトを平行移動させる（歩きアニメ無しのスライド移動）。
/// AutoPingPong = 開始位置から左右に自動往復。Keyboard = 矢印/AD キーで左右移動。
/// </summary>
public class SimpleMover : MonoBehaviour
{
    public enum Mode { AutoPingPong, Keyboard }

    [Tooltip("AutoPingPong=自動で往復 / Keyboard=キーで動かす")]
    public Mode mode = Mode.AutoPingPong;
    [Tooltip("移動の速さ（ワールド単位/秒）")]
    public float speed = 1.2f;
    [Tooltip("移動する向き（既定は左右＝X軸）")]
    public Vector3 direction = Vector3.right;
    [Tooltip("AutoPingPong時：開始位置から±この距離を往復")]
    public float range = 2.5f;

    Vector3 _start;

    void Awake() { _start = transform.position; }

    void Update()
    {
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
        if (mode == Mode.AutoPingPong)
        {
            float w = speed / Mathf.Max(range, 0.01f);
            float offset = Mathf.Sin(Time.time * w) * range;
            transform.position = _start + dir * offset;
        }
        else
        {
            transform.position += dir * (ReadHorizontal() * speed * Time.deltaTime);
        }
    }

    float ReadHorizontal()
    {
#if ENABLE_INPUT_SYSTEM
        float v = 0f;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) v -= 1f;
            if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) v += 1f;
        }
        return v;
#else
        return Input.GetAxisRaw("Horizontal");
#endif
    }
}
