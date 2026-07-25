using UnityEngine;

/// <summary>
/// 対象(人)を、斜め上・後ろから見下ろして追いかけるカメラ。
/// followFacing=false のときは固定アングル（体が向きを変えてもカメラは回り込まない）。
/// followFacing=true にすると体の向きに合わせてカメラも背後へ回り込む。
/// </summary>
public class FollowCamera : MonoBehaviour
{
    [Tooltip("追いかける対象（人）")]
    public Transform target;
    [Tooltip("対象から見たカメラ位置のオフセット。z=-で後ろ、y=+で上。")]
    public Vector3 offset = new Vector3(0f, 3.2f, -2.1f);
    [Tooltip("対象のどの高さを見るか（足元からの高さ）。")]
    public float lookHeight = 0.9f;
    [Tooltip("追従の滑らかさ。大きいほど速く追いつく。0で即追従。")]
    [Range(0f, 20f)] public float smooth = 6f;
    [Tooltip("ONにすると体の向きに合わせてカメラも回り込む。OFFで固定アングル。")]
    public bool followFacing = false;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 off = followFacing ? target.rotation * offset : offset;
        Vector3 desired = target.position + off;
        Vector3 aim = target.position + Vector3.up * lookHeight;
        if (Application.isPlaying && smooth > 0f)
        {
            float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, k);
            Quaternion look = Quaternion.LookRotation(aim - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, k);
        }
        else
        {
            transform.position = desired;
            transform.rotation = Quaternion.LookRotation(aim - desired);
        }
    }
}
