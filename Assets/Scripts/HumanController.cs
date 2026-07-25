using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 人の状態切り替え。M=歩く / N=椅子 / B=シャワー / V=マットで寝転ぶ / C=窓で外を眺める。
/// N/B/V/C を押すと、その場所まで歩いて行ってからアクションする。
/// </summary>
public class HumanController : MonoBehaviour
{
    public enum State { Walking, GoingTo, Sitting, Showering, LyingDown, Leaning }
    public State state = State.Walking;

    [Header("場所マーカー")]
    public Transform seatPoint;    // 椅子
    public Transform showerPoint;  // シャワー（立つ位置）
    public Transform matPoint;     // マット（寝る位置・向き）
    public Transform windowPoint;  // 窓（立つ位置・向き）
    public Transform showerWater;  // シャワーの水パーティクル（ON/OFF切替）

    [Header("ポーズ角度")]
    public float hipSitAngle = -90f;
    public float kneeSitAngle = 90f;
    public float armRaiseAngle = -165f;
    public float armLeanAngle = -80f;  // 窓枠に肘をつく（腕を前へ）

    [Header("音")]
    public AudioClip sitSound;
    [Range(0f, 1f)] public float sitVolume = 1f;

    [Header("挙動")]
    public float transitionSpeed = 8f;
    public float walkSpeed = 1.2f;
    public float arriveDist = 0.12f;
    public float walkY = -0.90f;
    public float walkZ = -1.63f;

    SimpleMover _mover;
    ProceduralWalker _walker;
    Transform _hipL, _hipR, _kneeL, _kneeR, _armL, _armR;
    Transform _target;
    State _pending;

    void Awake()
    {
        _mover = GetComponent<SimpleMover>();
        _walker = GetComponent<ProceduralWalker>();
        _hipL = transform.Find("LegL_Pivot"); _hipR = transform.Find("LegR_Pivot");
        _kneeL = _hipL ? _hipL.Find("LegL_Knee") : null;
        _kneeR = _hipR ? _hipR.Find("LegR_Knee") : null;
        _armL = transform.Find("ArmL_Pivot"); _armR = transform.Find("ArmR_Pivot");
        SetWater(false);
        SetWalkComponents(state == State.Walking);
    }

    void Update()
    {
        if (KeyDown("M")) GoWalk();
        else if (state != State.GoingTo)
        {
            if (KeyDown("N") && seatPoint) StartGoing(seatPoint, State.Sitting);
            else if (KeyDown("B") && showerPoint) StartGoing(showerPoint, State.Showering);
            else if (KeyDown("V") && matPoint) StartGoing(matPoint, State.LyingDown);
            else if (KeyDown("C") && windowPoint) StartGoing(windowPoint, State.Leaning);
        }

        float k = 1f - Mathf.Exp(-transitionSpeed * Time.deltaTime);

        if (state == State.GoingTo)
        {
            Vector3 tp = new Vector3(_target.position.x, transform.position.y, _target.position.z);
            transform.position = Vector3.MoveTowards(transform.position, tp, walkSpeed * Time.deltaTime);
            if (new Vector2(tp.x - transform.position.x, tp.z - transform.position.z).magnitude < arriveDist)
                EnterAction();
        }
        else if (state == State.Sitting)
        {
            LerpTo(seatPoint, k);
            Slerp(_hipL, Quaternion.Euler(hipSitAngle, 0, 0), k); Slerp(_hipR, Quaternion.Euler(hipSitAngle, 0, 0), k);
            Slerp(_kneeL, Quaternion.Euler(kneeSitAngle, 0, 0), k); Slerp(_kneeR, Quaternion.Euler(kneeSitAngle, 0, 0), k);
        }
        else if (state == State.Showering)
        {
            LerpTo(showerPoint, k);
            Slerp(_armL, Quaternion.Euler(armRaiseAngle, 0, 0), k); Slerp(_armR, Quaternion.Euler(armRaiseAngle, 0, 0), k);
        }
        else if (state == State.LyingDown)
        {
            LerpTo(matPoint, k);
        }
        else if (state == State.Leaning)
        {
            LerpTo(windowPoint, k);
            Slerp(_armL, Quaternion.Euler(armLeanAngle, 0, 0), k); Slerp(_armR, Quaternion.Euler(armLeanAngle, 0, 0), k);
        }
    }

    void StartGoing(Transform target, State pending)
    {
        state = State.GoingTo; _target = target; _pending = pending;
        SetWater(false); ResetLegs(); ResetArms();
        Vector3 p = transform.position; p.y = walkY; transform.position = p;
        if (_mover) _mover.enabled = false;
        if (_walker) _walker.enabled = true;
    }

    void EnterAction()
    {
        state = _pending;
        if (_walker) _walker.enabled = false;
        if (_mover) _mover.enabled = false;
        if (state == State.Sitting && sitSound != null)
            AudioSource.PlayClipAtPoint(sitSound, Camera.main ? Camera.main.transform.position : transform.position, sitVolume);
        if (state == State.Showering) SetWater(true);
    }

    void GoWalk()
    {
        state = State.Walking; SetWater(false);
        Vector3 p = transform.position; p.y = walkY; p.z = walkZ; transform.position = p;
        transform.rotation = Quaternion.identity;
        ResetLegs(); ResetArms();
        SetWalkComponents(true);
    }

    void LerpTo(Transform pt, float k)
    {
        if (pt == null) return;
        transform.position = Vector3.Lerp(transform.position, pt.position, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, pt.rotation, k);
    }

    void ResetLegs() { Set(_hipL); Set(_hipR); Set(_kneeL); Set(_kneeR); }
    void ResetArms() { Set(_armL); Set(_armR); }
    void Set(Transform t) { if (t) t.localRotation = Quaternion.identity; }
    void SetWater(bool on) { if (showerWater) showerWater.gameObject.SetActive(on); }
    void SetWalkComponents(bool on) { if (_walker) _walker.enabled = on; if (_mover) _mover.enabled = on; }
    void Slerp(Transform t, Quaternion to, float k) { if (t) t.localRotation = Quaternion.Slerp(t.localRotation, to, k); }

    bool KeyDown(string key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current; if (kb == null) return false;
        switch (key) { case "M": return kb.mKey.wasPressedThisFrame; case "N": return kb.nKey.wasPressedThisFrame;
            case "B": return kb.bKey.wasPressedThisFrame; case "V": return kb.vKey.wasPressedThisFrame;
            case "C": return kb.cKey.wasPressedThisFrame; }
        return false;
#else
        switch (key) { case "M": return Input.GetKeyDown(KeyCode.M); case "N": return Input.GetKeyDown(KeyCode.N);
            case "B": return Input.GetKeyDown(KeyCode.B); case "V": return Input.GetKeyDown(KeyCode.V);
            case "C": return Input.GetKeyDown(KeyCode.C); }
        return false;
#endif
    }
}
