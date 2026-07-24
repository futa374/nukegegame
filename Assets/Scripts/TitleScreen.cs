using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// ゲーム開始時のタイトル画面。真っ黒背景に「Hairarium」等を表示し、
/// 何かキー／クリックでゲームを開始する。開始前は Active=false で他の操作を止める。
/// ※ OnGUI + 内蔵フォントのため、WebGL では日本語は描画されない（エディタ/スタンドアロン向け）。
/// Main Camera にアタッチ。
/// </summary>
public class TitleScreen : MonoBehaviour
{
    public static bool Active { get; private set; }

    [Header("文言")]
    public string title = "Hairarium";
    public string subtitle = "部屋に落ちた抜け毛たちを集めろ";
    public string prompt = "Press any Key";

    [Header("文字サイズ")]
    public int titleFontSize = 110;
    public int subtitleFontSize = 40;
    public int promptFontSize = 30;

    bool _started;
    int _startedFrame = -1;

    GUIStyle _titleStyle, _subStyle, _promptStyle;
    Texture2D _blackTex;

    void Awake()
    {
        Active = false;
        _started = false;
        _blackTex = new Texture2D(1, 1);
        _blackTex.SetPixel(0, 0, Color.black);
        _blackTex.Apply();
    }

    void Update()
    {
        if (!_started)
        {
            if (AnyPressedThisFrame())
            {
                _started = true;
                _startedFrame = Time.frameCount;
            }
            return;
        }

        if (!Active && Time.frameCount > _startedFrame)
            Active = true;
    }

    void OnGUI()
    {
        if (_started) return;

        EnsureStyles();

        Color prev = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _blackTex);
        GUI.color = prev;

        float cy = Screen.height * 0.5f;
        float w = Screen.width;

        float titleH = titleFontSize * 1.4f;
        GUI.Label(new Rect(0, cy - titleH, w, titleH), title, _titleStyle);

        float subH = subtitleFontSize * 1.6f;
        GUI.Label(new Rect(0, cy + 4f, w, subH), subtitle, _subStyle);

        float blink = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2f));
        Color pc = _promptStyle.normal.textColor;
        _promptStyle.normal.textColor = new Color(pc.r, pc.g, pc.b, blink);
        float promptH = promptFontSize * 2f;
        GUI.Label(new Rect(0, cy + subH + promptFontSize, w, promptH), prompt, _promptStyle);
        _promptStyle.normal.textColor = pc;
    }

    void EnsureStyles()
    {
        if (_titleStyle == null)
            _titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        if (_subStyle == null)
            _subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal };
        if (_promptStyle == null)
            _promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Normal };

        _titleStyle.fontSize = titleFontSize;
        _titleStyle.normal.textColor = Color.white;
        _subStyle.fontSize = subtitleFontSize;
        _subStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        _promptStyle.fontSize = promptFontSize;
        _promptStyle.normal.textColor = Color.white;
    }

    bool AnyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        bool any = false;
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) any = true;
        if (Mouse.current != null &&
            (Mouse.current.leftButton.wasPressedThisFrame ||
             Mouse.current.rightButton.wasPressedThisFrame ||
             Mouse.current.middleButton.wasPressedThisFrame)) any = true;
        return any;
#else
        return Input.anyKeyDown;
#endif
    }
}
