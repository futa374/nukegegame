using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// planet シーン用の周回カメラ。
/// 通常モード: 地球中心をドラッグ回転 + スクロールズーム。
/// 追従モード: クリックした毛の周りを回転しながら、毛の動きについていく。
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

    [Header("追従モード")]
    public float followDistance = 0.25f;   // 毛の周りを回る半径
    public float transitionTime = 0.4f;    // 移動の滑らかさ（小さいほどギュン）
    public float autoOrbitSpeedDeg = 40f;  // 追従中の自動回転速度（度/秒）

    Vector3 _basePivot;
    float _baseDistance;

    Transform _followTarget;
    Vector3 _currentPivot;
    float _currentDistance;
    Vector3 _pivotVelocity;
    float _distanceVelocity;
    bool _following;

    void Start()
    {
        _basePivot        = pivot;
        _baseDistance     = distance;
        _currentPivot     = pivot;
        _currentDistance  = distance;
        Apply();
    }

    // 外部から呼ぶ: 毛を追従開始
    public void FollowHair(Transform target)
    {
        _followTarget = target;
        _following = true;
    }

    // 外部から呼ぶ: 追従解除 → 地球ビューに戻る
    public void ExitFollow()
    {
        _followTarget = null;
        _following = false;
    }

    public bool IsFollowing => _following;

    void Update()
    {
        // 追従中に毛が消えたら自動解除
        if (_following && (_followTarget == null || !_followTarget.gameObject.activeInHierarchy))
            ExitFollow();

        if (_following) azimuth += autoOrbitSpeedDeg * Time.deltaTime;
        HandleInput();

        // ターゲットのpivotとdistanceを決定
        Vector3 targetPivot   = _following ? _followTarget.position : _basePivot;
        float   targetDist    = _following ? followDistance : _baseDistance;

        // SmoothDampでギュンと移動
        _currentPivot    = Vector3.SmoothDamp(_currentPivot, targetPivot, ref _pivotVelocity, transitionTime);
        _currentDistance = Mathf.SmoothDamp(_currentDistance, targetDist, ref _distanceVelocity, transitionTime);

        Apply();
    }

    void HandleInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue();
            azimuth   += d.x * rotateSpeed;
            elevation -= d.y * rotateSpeed;
            elevation  = Mathf.Clamp(elevation, minElevation, maxElevation);
        }

        float sy = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(sy) > 0.001f)
        {
            if (_following)
            {
                followDistance -= sy * zoomSpeed * 0.1f;
                followDistance  = Mathf.Clamp(followDistance, 0.05f, 1f);
            }
            else
            {
                distance -= sy * zoomSpeed;
                distance  = Mathf.Clamp(distance, minDistance, maxDistance);
                _baseDistance = distance;
            }
        }
    }

    void Apply()
    {
        float az = azimuth * Mathf.Deg2Rad, el = elevation * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(
            Mathf.Cos(el) * Mathf.Sin(az),
            Mathf.Sin(el),
            Mathf.Cos(el) * Mathf.Cos(az));
        transform.position = _currentPivot + dir * _currentDistance;
        transform.rotation = Quaternion.LookRotation(_currentPivot - transform.position, Vector3.up);
    }
}
