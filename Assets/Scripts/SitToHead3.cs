using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// head シーンで人が椅子に座ると、カメラを head3 シーンと同じアングルへ寄せていき、
/// 画面をクリックすると head3 シーンへ「ぱっと」切り替わる。
///
/// head の座った人と head3 の被写体（同じく椅子に座った Human）を同じフレーミングで映すことで、
/// 「座る → head3」が、同じ椅子に別のものが座っている入れ替え＝マッチカットとして読める。
/// HeadToHairChair（頭クリック→頭皮の毛が椅子に座る画面）と同じ入れ子の構図を、
/// シーンをまたいで一段外側に開いたもの。
///
/// 既存スクリプトには一切手を加えない独立ファイル。head シーンの Main Camera にアタッチして使う。
/// </summary>
[RequireComponent(typeof(Camera))]
public class SitToHead3 : MonoBehaviour
{
    [Header("参照（未設定なら自動取得）")]
    [Tooltip("座り状態を監視する人。未設定ならシーンから探す。")]
    public HumanController human;
    [Tooltip("アングルを操作するカメラ。未設定なら同じ GameObject / Main Camera から探す。")]
    public FollowCamera followCam;

    [Header("head3 と同じアングル")]
    [Tooltip("head3 の FollowCamera と同じオフセット。")]
    public Vector3 head3Offset = new Vector3(2.4f, 1.25f, -2.6f);
    [Tooltip("head3 の FollowCamera と同じ注視高さ。")]
    public float head3LookHeight = 0.85f;
    [Tooltip("アングルを寄せていく速さ。大きいほど速く head3 の画角になる。")]
    public float retargetSpeed = 2.5f;

    [Header("遷移")]
    [Tooltip("クリックで切り替わる先のシーン名。")]
    public string head3Scene = "head3";
    [Tooltip("座ってからクリックを受け付けるまでの待ち（秒）。アングルが落ち着くまでの間。")]
    public float clickArmDelay = 0.8f;
    [Tooltip("クリックできることを示す ◎ を人の上に表示する。")]
    public bool showPrompt = true;

    Camera _cam;
    Vector3 _origOffset;
    float _origLookHeight;
    bool _captured;      // 元アングルを保存済み
    bool _sitting;       // 座り状態に入った
    bool _armed;         // クリック受付中
    float _sitEnterAt = -1f;

    void Start()
    {
        _cam = GetComponent<Camera>();

        if (human == null) human = FindObjectOfType<HumanController>();
        if (followCam == null)
        {
            followCam = GetComponent<FollowCamera>();
            if (followCam == null && Camera.main != null)
                followCam = Camera.main.GetComponent<FollowCamera>();
        }

        if (followCam != null)
        {
            _origOffset = followCam.offset;
            _origLookHeight = followCam.lookHeight;
            _captured = true;
        }
    }

    void Update()
    {
        if (human == null || followCam == null) return;
        if (!_captured)
        {
            _origOffset = followCam.offset;
            _origLookHeight = followCam.lookHeight;
            _captured = true;
        }

        bool isSitting = human.state == HumanController.State.Sitting;

        if (isSitting && !_sitting)
        {
            _sitting = true;
            _sitEnterAt = Time.time;
        }
        else if (!isSitting && _sitting)
        {
            // 立ち上がった／歩き出した → 元のアングルへ戻し、遷移も解除
            _sitting = false;
            _armed = false;
            _sitEnterAt = -1f;
        }

        // アングルを head3 側へ寄せる／座っていなければ元へ戻す。
        // FollowCamera が毎フレーム offset を使って位置を出すので、値を補間するだけで滑らかに動く。
        Vector3 tgtOffset = _sitting ? head3Offset : _origOffset;
        float tgtLook = _sitting ? head3LookHeight : _origLookHeight;
        float k = 1f - Mathf.Exp(-retargetSpeed * Time.deltaTime);
        followCam.offset = Vector3.Lerp(followCam.offset, tgtOffset, k);
        followCam.lookHeight = Mathf.Lerp(followCam.lookHeight, tgtLook, k);

        if (_sitting && !_armed && Time.time - _sitEnterAt >= clickArmDelay)
            _armed = true;

        if (_armed && ClickedThisFrame() && !string.IsNullOrEmpty(head3Scene))
        {
            _armed = false; // 二重遷移防止
            SceneTransitioner.Get().TransitionTo(head3Scene);
        }
    }

    void OnGUI()
    {
        if (!showPrompt || !_armed || _cam == null) return;

        // 座っている人の少し上に ◎ を出す
        Vector3 world = human != null ? human.transform.position + Vector3.up * 1.1f
                                      : transform.position + transform.forward * 3f;
        Vector3 sp = _cam.WorldToScreenPoint(world);
        if (sp.z < 0f) return;

        float scale = Mathf.Abs(Mathf.Sin(Time.time * 2.5f));
        int fontSize = Mathf.RoundToInt(120f * scale);
        if (fontSize < 1) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = Color.black;

        float w = 200f, h = 200f;
        float guiY = Screen.height - sp.y;
        GUI.Label(new Rect(sp.x - w * 0.5f, guiY - h * 0.5f, w, h), "◎", style);
    }

    bool ClickedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }
}
