using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 人の状態切り替え。
/// M=歩く / N=椅子まで歩いて座る / B=シャワーを浴びる / V=寝転ぶ。
/// </summary>
public class HumanController : MonoBehaviour
{
    public enum State { Walking, GoingToChair, Sitting, Showering, LyingDown }
    public State state = State.Walking;

    [Header("椅子（座る）")]
    public Transform seatPoint;
    public float hipSitAngle = -90f;
    public float kneeSitAngle = 90f;
    public AudioClip sitSound;
    [Range(0f, 1f)] public float sitVolume = 1f;
    public float walkSpeed = 1.2f;
    public float arriveDist = 0.12f;

    [Header("シャワー（B）")]
    public float armRaiseAngle = -165f;   // 腕を上げる角度

    [Header("寝転ぶ（V）")]
    public float lyingPitch = -90f;        // 体を倒す角度
    public float lyingY = -0.74f;           // 寝たときの高さ

    [Header("共通")]
    public float transitionSpeed = 8f;
    public float walkY = -0.90f;
    public float walkZ = -1.63f;

    SimpleMover _mover;
    ProceduralWalker _walker;
    Transform _hipL, _hipR, _kneeL, _kneeR, _armL, _armR, _showerRig;

    void Awake()
    {
        _mover = GetComponent<SimpleMover>();
        _walker = GetComponent<ProceduralWalker>();
        _hipL = transform.Find("LegL_Pivot"); _hipR = transform.Find("LegR_Pivot");
        _kneeL = _hipL ? _hipL.Find("LegL_Knee") : null;
        _kneeR = _hipR ? _hipR.Find("LegR_Knee") : null;
        _armL = transform.Find("ArmL_Pivot"); _armR = transform.Find("ArmR_Pivot");
        _showerRig = transform.Find("ShowerRig");
        SetShower(false);
        SetWalkComponents(state == State.Walking);
    }

    void Update()
    {
        if (KeyDown("M")) GoWalk();
        else if (KeyDown("N") && state == State.Walking) GoToChair();
        else if (KeyDown("B")) GoShower();
        else if (KeyDown("V")) GoLie();

        float k = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);

        if (state == State.GoingToChair)
        {
            if (seatPoint == null) { StartSitting(); return; }
            Vector3 target = new Vector3(seatPoint.position.x, transform.position.y, seatPoint.position.z);
            transform.position = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
            if (new Vector2(target.x - transform.position.x, target.z - transform.position.z).magnitude < arriveDist) StartSitting();
        }
        else if (state == State.Sitting)
        {
            if (seatPoint != null)
            {
                transform.position = Vector3.Lerp(transform.position, seatPoint.position, k);
                transform.rotation = Quaternion.Slerp(transform.rotation, seatPoint.rotation, k);
            }
            Slerp(_hipL, Quaternion.Euler(hipSitAngle, 0, 0), k); Slerp(_hipR, Quaternion.Euler(hipSitAngle, 0, 0), k);
            Slerp(_kneeL, Quaternion.Euler(kneeSitAngle, 0, 0), k); Slerp(_kneeR, Quaternion.Euler(kneeSitAngle, 0, 0), k);
        }
        else if (state == State.Showering)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.identity, k);
            Slerp(_armL, Quaternion.Euler(armRaiseAngle, 0, 0), k); Slerp(_armR, Quaternion.Euler(armRaiseAngle, 0, 0), k);
        }
        else if (state == State.LyingDown)
        {
            Quaternion lie = Quaternion.Euler(lyingPitch, 0, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, lie, k);
            Vector3 tp = new Vector3(transform.position.x, lyingY, transform.position.z);
            transform.position = Vector3.Lerp(transform.position, tp, k);
        }
    }

    void GoToChair() { state = State.GoingToChair; SetShower(false); ResetArms(); SetWalkComponents(false); if (_walker) _walker.enabled = true; }

    void StartSitting()
    {
        state = State.Sitting; SetWalkComponents(false);
        if (sitSound != null) AudioSource.PlayClipAtPoint(sitSound, Camera.main ? Camera.main.transform.position : transform.position, sitVolume);
    }

    void GoShower()
    {
        state = State.Showering; SetWalkComponents(false); ResetLegs();
        Vector3 p = transform.position; p.y = walkY; transform.position = p;
        SetShower(true);
    }

    void GoLie()
    {
        state = State.LyingDown; SetWalkComponents(false); SetShower(false); ResetLegs(); ResetArms();
    }

    void GoWalk()
    {
        state = State.Walking; SetShower(false);
        Vector3 p = transform.position; p.y = walkY; p.z = walkZ; transform.position = p;
        transform.rotation = Quaternion.identity;
        ResetLegs(); ResetArms();
        SetWalkComponents(true);
    }

    void ResetLegs() { Set(_hipL); Set(_hipR); Set(_kneeL); Set(_kneeR); }
    void ResetArms() { Set(_armL); Set(_armR); }
    void Set(Transform t) { if (t) t.localRotation = Quaternion.identity; }
    void SetShower(bool on) { if (_showerRig) _showerRig.gameObject.SetActive(on); }
    void SetWalkComponents(bool on) { if (_walker) _walker.enabled = on; if (_mover) _mover.enabled = on; }
    void Slerp(Transform t, Quaternion to, float k) { if (t) t.localRotation = Quaternion.Slerp(t.localRotation, to, k); }

    bool KeyDown(string key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current; if (kb == null) return false;
        switch (key) { case "M": return kb.mKey.wasPressedThisFrame; case "N": return kb.nKey.wasPressedThisFrame;
            case "B": return kb.bKey.wasPressedThisFrame; case "V": return kb.vKey.wasPressedThisFrame; }
        return false;
#else
        switch (key) { case "M": return Input.GetKeyDown(KeyCode.M); case "N": return Input.GetKeyDown(KeyCode.N);
            case "B": return Input.GetKeyDown(KeyCode.B); case "V": return Input.GetKeyDown(KeyCode.V); }
        return false;
#endif
    }
}
