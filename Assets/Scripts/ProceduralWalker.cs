using UnityEngine;

/// <summary>
/// 骨の無い箱人間用の簡易歩行モーション。
/// ・進行方向へ体を向ける（端で折り返すと即座にUターン）
/// ・付け根ピボット LegL_Pivot / LegR_Pivot / ArmL_Pivot / ArmR_Pivot を前後に交互に振る
/// ※ピボットの回転のみを操作し、位置は一切変更しない（ズレ防止）。SimpleMover と併用。
/// </summary>
public class ProceduralWalker : MonoBehaviour
{
    [Tooltip("腕・脚の振れ幅（度）")]
    public float swingAngle = 30f;
    [Tooltip("歩幅の細かさ（移動距離あたりの周期）。大きいほど小刻み。")]
    public float cadence = 6f;
    [Tooltip("これ以上の速さで“歩行中”とみなす（手足の振り用）")]
    public float moveThreshold = 0.05f;
    [Tooltip("Uターンの速さ。大きいほどキビキビ即座に回る。")]
    public float turnSpeed = 16f;
    [Tooltip("腕の前後を脚と入れ替える（腕の向きが逆に見えるとき）")]
    public bool swapArmPhase = false;

    Transform _legL, _legR, _armL, _armR;
    Vector3 _lastPos;
    float _phase;
    Vector3 _faceDir = Vector3.forward;

    void Awake()
    {
        _lastPos = transform.position;
        _faceDir = transform.forward;
        _legL = transform.Find("LegL_Pivot");
        _legR = transform.Find("LegR_Pivot");
        _armL = transform.Find("ArmL_Pivot");
        _armR = transform.Find("ArmR_Pivot");
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 d = transform.position - _lastPos;
        _lastPos = transform.position;
        Vector3 flat = new Vector3(d.x, 0f, d.z);
        float dist = flat.magnitude;
        float speed = dt > 0f ? dist / dt : 0f;

        // 進行方向へ体を向ける：ほんの少しでも動いたら即その向きを採用（折り返し直後に回り始める）
        if (flat.sqrMagnitude > 1e-8f)
            _faceDir = flat.normalized;
        Quaternion targetRot = Quaternion.LookRotation(_faceDir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-turnSpeed * dt));

        // 手足を前後に振る（ピボットのローカルX軸まわり）
        _phase += dist * cadence;
        float amp = Mathf.Clamp01(speed / Mathf.Max(moveThreshold * 6f, 0.001f)) * swingAngle;
        float s = Mathf.Sin(_phase) * amp;

        float armSign = swapArmPhase ? 1f : -1f;
        Apply(_legL, s);
        Apply(_legR, -s);
        Apply(_armL, armSign * s);
        Apply(_armR, -armSign * s);
    }

    void Apply(Transform pivot, float angle)
    {
        if (pivot != null) pivot.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }
}
