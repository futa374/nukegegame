using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 抜け毛を指（マウス/タッチ）で触れる・撫でると：毛先が揺れ／効果音が鳴り／口が開く。
/// 毛オブジェクト（Collider付き）にアタッチ。揺れは「毛の根元」を支点に回転させるので毛先が一番動く。
/// Main Camera に "MainCamera" タグが必要。新旧 Input System / マウス・タッチ 両対応。
/// </summary>
public class HairWobble : MonoBehaviour
{
    [Header("毛の揺れ（根元支点）")]
    public float stiffness = 200f;
    public float damping = 6f;
    public float kickStrength = 1.0f;
    public float maxAngle = 40f;
    [Tooltip("根元(支点)の位置。未指定なら見た目の下端を自動で根元にする。")]
    public Transform baseOverride;

    [Header("効果音")]
    public AudioClip touchSound;
    [Range(0f, 1f)] public float volume = 1f;
    public float soundCooldown = 0.06f;

    [Header("口が開く")]
    [Tooltip("触れたときに開く口オブジェクト（このシーンでは Sphere (2)）。")]
    public Transform mouth;
    [Tooltip("開いたときの伸び具合(x,y,z)。yを大きくすると縦に開く。")]
    public Vector3 mouthOpenScale = new Vector3(0.3f, 1.1f, 0.3f);
    public float mouthStiffness = 120f;
    public float mouthDamping = 7f;

    [Header("触れ判定")]
    public float touchRadius = 0.5f;
    public bool debugLog = false;

    Camera _cam;
    AudioSource _audio;
    Vector3 _basePoint;
    Quaternion _baseRot;
    Vector3 _baseOffset;
    Vector2 _bend, _bendVel, _lastPointer;
    bool _tracking, _wasOver;
    float _lastSoundTime = -999f;
    Vector3 _mouthBaseScale = Vector3.one;
    float _open, _openVel;

    void Awake()
    {
        _cam = Camera.main;
        ComputeBase();
        EnsureCollider();
        EnsureAudio();
        if (mouth != null) _mouthBaseScale = mouth.localScale;
    }

    void ComputeBase()
    {
        _baseRot = transform.rotation;
        if (baseOverride != null) _basePoint = baseOverride.position;
        else
        {
            Renderer r = GetComponentInChildren<Renderer>();
            _basePoint = r != null
                ? new Vector3(r.bounds.center.x, r.bounds.min.y, r.bounds.center.z)
                : transform.position;
        }
        _baseOffset = transform.position - _basePoint;
    }

    void EnsureAudio()
    {
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        if (_audio.clip == null && touchSound != null) _audio.clip = touchSound;
    }

    void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null) return;
        Renderer rend = GetComponentInChildren<Renderer>();
        GameObject target = rend ? rend.gameObject : gameObject;
        BoxCollider box = target.AddComponent<BoxCollider>();
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf && mf.sharedMesh) { box.center = mf.sharedMesh.bounds.center; box.size = mf.sharedMesh.bounds.size + Vector3.one * 0.15f; }
        else box.size = Vector3.one * 0.5f;
    }

    void Update()
    {
        if (_cam == null) _cam = Camera.main;

        bool over = false;
        if (_cam != null && PointerHeld())
        {
            Vector2 p = PointerPos();
            Ray ray = _cam.ScreenPointToRay(p);
            over = Physics.SphereCast(ray, touchRadius, out RaycastHit hit, Mathf.Infinity) && IsMine(hit.collider);
            if (over)
            {
                if (PointerPressedThisFrame() || !_wasOver) Trigger();
                Vector2 delta = _tracking ? (p - _lastPointer) : Vector2.zero;
                if (delta.sqrMagnitude > 4f) _bendVel += new Vector2(delta.y, delta.x) * (6f * kickStrength);
                _lastPointer = p; _tracking = true;
            }
            else { _lastPointer = p; _tracking = false; }
        }
        else _tracking = false;
        _wasOver = over;

        float dt = Time.deltaTime;

        // 毛：根元を支点に回転（減衰振動）
        _bendVel = Vector2.ClampMagnitude(_bendVel, 1400f);
        Vector2 acc = -stiffness * _bend - damping * _bendVel;
        _bendVel += acc * dt;
        _bend += _bendVel * dt;
        _bend = Vector2.ClampMagnitude(_bend, maxAngle);
        Quaternion bendRot = Quaternion.Euler(_bend.x, 0f, _bend.y);
        transform.rotation = bendRot * _baseRot;
        transform.position = _basePoint + bendRot * _baseOffset;

        // 口：開いて閉じる（減衰振動）
        if (mouth != null)
        {
            float macc = -mouthStiffness * _open - mouthDamping * _openVel;
            _openVel += macc * dt;
            _open += _openVel * dt;
            if (_open < 0f) { _open = 0f; if (_openVel < 0f) _openVel = 0f; }
            _open = Mathf.Min(_open, 1.4f);
            mouth.localScale = new Vector3(
                _mouthBaseScale.x * (1f + _open * mouthOpenScale.x),
                _mouthBaseScale.y * (1f + _open * mouthOpenScale.y),
                _mouthBaseScale.z * (1f + _open * mouthOpenScale.z));
        }
    }

    void Trigger()
    {
        _bendVel += new Vector2(0f, 280f) * kickStrength; // 毛を弾く
        _openVel += 11f;                                  // 口を開く
        if (touchSound != null && _audio != null && Time.unscaledTime - _lastSoundTime >= soundCooldown)
        {
            _audio.PlayOneShot(touchSound, volume);
            _lastSoundTime = Time.unscaledTime;
        }
        if (debugLog) Debug.Log("[HairWobble] Trigger! sound=" + (touchSound ? touchSound.name : "none") + " mouth=" + (mouth ? mouth.name : "none"), this);
    }

    bool IsMine(Collider c) => c != null && (c.transform == transform || c.transform.IsChildOf(transform));

    bool PointerHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Pointer.current != null && Pointer.current.press.isPressed;
#else
        if (Input.touchCount > 0) return true;
        return Input.GetMouseButton(0);
#endif
    }

    bool PointerPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
#else
        if (Input.touchCount > 0) return Input.GetTouch(0).phase == TouchPhase.Began;
        return Input.GetMouseButtonDown(0);
#endif
    }

    Vector2 PointerPos()
    {
#if ENABLE_INPUT_SYSTEM
        return Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
#else
        if (Input.touchCount > 0) return Input.GetTouch(0).position;
        return (Vector2)Input.mousePosition;
#endif
    }
}
