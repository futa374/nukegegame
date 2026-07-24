using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// カメラからクリック方向に太さのあるレイ（SphereCast）を飛ばし、tag "hair" に当たったら
/// その毛を「発見済み」として記録。発見時に画面中央へ新種発見トーストを表示する。
/// 画面右上に「発見数 / 総数」、左下に操作方法を表示。新旧 Input System 両対応。
/// タイトル画面中（TitleScreen.Active==false）は反応しない。
/// ※ OnGUI + 内蔵フォントのため、WebGL では日本語は描画されない（エディタ/スタンドアロン向け）。
/// Main Camera にアタッチ。対象には Collider が必要。
/// </summary>
[RequireComponent(typeof(Camera))]
public class HairClickLogger : MonoBehaviour
{
    [Header("判定")]
    public string hairTag = "hair";
    [Tooltip("当たり判定の太さ（ワールド単位）。大きいほどクリックが緩くなる。")]
    public float clickRadius = 1.0f;

    [Header("カウンターUI（右上）")]
    public int fontSize = 96;
    public Color textColor = new Color(1f, 0.85f, 0.2f);
    public string caption = "HAIR";
    public Color panelColor = new Color(0f, 0f, 0f, 0.6f);
    [Tooltip("画面端からの余白（ピクセル）。")]
    public float margin = 28f;

    [Header("操作説明UI（左下）")]
    public bool showControls = true;
    public int controlsFontSize = 30;
    [TextArea]
    public string[] controls = new string[]
    {
        "左ドラッグ ： 移動",
        "ピンチ / スクロール ： ズーム",
        "毛をクリック ： 発見",
    };

    [Header("新種発見トースト（中央）")]
    [Tooltip("毛に割り当てる題名プール。ランダム（重複なし）で割り当てる。")]
    public string[] speciesNames = new string[]
    {
        "コップノヘリノカミ",
        "ウキワノカミ",
        "エックスノカミ",
        "イトマキノカミ",
        "ストレートノカミ",
        "フケツキノカミ",
        "カフンノカミ",
        "キョクセンノカミ",
    };
    [Tooltip("トーストの表示時間（秒）。")]
    public float toastDuration = 2f;
    public int toastFontSize = 46;
    public Color toastPanelColor = new Color(0f, 0f, 0f, 0.82f);

    Camera _cam;
    int _total;
    readonly HashSet<int> _found = new HashSet<int>();
    readonly RaycastHit[] _hits = new RaycastHit[32];

    List<string> _namePool;
    int _nameIndex;
    string _toastText;
    float _toastTimer;

    GUIStyle _numStyle, _capStyle, _ctrlStyle, _toastStyle;
    Texture2D _panelTex;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        _panelTex = new Texture2D(1, 1);
        _panelTex.SetPixel(0, 0, Color.white);
        _panelTex.Apply();
    }

    void Start()
    {
        _total = GameObject.FindGameObjectsWithTag(hairTag).Length;

        _namePool = new List<string>(speciesNames);
        for (int i = _namePool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_namePool[i], _namePool[j]) = (_namePool[j], _namePool[i]);
        }
    }

    void Update()
    {
        if (!TitleScreen.Active) return;

        if (_toastTimer > 0f)
            _toastTimer -= Time.unscaledDeltaTime;

        if (!LeftPressedThisFrame()) return;

        Ray ray = _cam.ScreenPointToRay(MousePosition());
        int count = Physics.SphereCastNonAlloc(ray, clickRadius, _hits, Mathf.Infinity);
        for (int i = 0; i < count; i++)
        {
            Collider col = _hits[i].collider;
            if (col != null && col.CompareTag(hairTag))
            {
                if (_found.Add(col.gameObject.GetInstanceID()))
                {
                    string name = NextName();
                    Debug.Log("find hair! : " + name);
                    _toastText = "新種抜け毛「" + name + "」を発見。図鑑に追加しました";
                    _toastTimer = toastDuration;
                }
                break;
            }
        }
    }

    string NextName()
    {
        if (_namePool == null || _namePool.Count == 0) return "ナナシノカミ";
        string n = _namePool[_nameIndex % _namePool.Count];
        _nameIndex++;
        return n;
    }

    void OnGUI()
    {
        EnsureStyles();
        if (TitleScreen.Active)
        {
            DrawCounter();
            if (showControls) DrawControls();
        }
        if (_toastTimer > 0f) DrawToast();
    }

    void DrawCounter()
    {
        string number = _found.Count + " / " + _total;
        bool hasCaption = !string.IsNullOrEmpty(caption);

        Vector2 numSize = _numStyle.CalcSize(new GUIContent(number));
        Vector2 capSize = hasCaption ? _capStyle.CalcSize(new GUIContent(caption)) : Vector2.zero;

        float padX = fontSize * 0.4f;
        float padY = fontSize * 0.25f;
        float contentW = Mathf.Max(numSize.x, capSize.x);
        float contentH = numSize.y + (hasCaption ? capSize.y : 0f);

        float boxW = contentW + padX * 2f;
        float boxH = contentH + padY * 2f;
        Rect box = new Rect(Screen.width - boxW - margin, margin, boxW, boxH);

        DrawPanel(box, panelColor);

        float y = box.y + padY;
        if (hasCaption)
        {
            GUI.Label(new Rect(box.x, y, box.width - padX, capSize.y), caption, _capStyle);
            y += capSize.y;
        }
        Rect numRect = new Rect(box.x, y, box.width - padX, numSize.y);
        Color save = _numStyle.normal.textColor;
        _numStyle.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
        GUI.Label(new Rect(numRect.x + 3f, numRect.y + 3f, numRect.width, numRect.height), number, _numStyle);
        _numStyle.normal.textColor = save;
        GUI.Label(numRect, number, _numStyle);
    }

    void DrawControls()
    {
        if (controls == null || controls.Length == 0) return;

        float lineH = _ctrlStyle.lineHeight > 0 ? _ctrlStyle.lineHeight : controlsFontSize * 1.2f;
        float maxW = 0f;
        for (int i = 0; i < controls.Length; i++)
        {
            float w = _ctrlStyle.CalcSize(new GUIContent(controls[i])).x;
            if (w > maxW) maxW = w;
        }

        float padX = controlsFontSize * 0.6f;
        float padY = controlsFontSize * 0.5f;
        float boxW = maxW + padX * 2f;
        float boxH = lineH * controls.Length + padY * 2f;
        Rect box = new Rect(margin, Screen.height - boxH - margin, boxW, boxH);

        DrawPanel(box, panelColor);

        float y = box.y + padY;
        for (int i = 0; i < controls.Length; i++)
        {
            GUI.Label(new Rect(box.x + padX, y, maxW, lineH), controls[i], _ctrlStyle);
            y += lineH;
        }
    }

    void DrawToast()
    {
        float a = Mathf.Clamp01(_toastTimer / 0.4f);

        GUIContent content = new GUIContent(_toastText);
        float maxW = Screen.width * 0.8f;
        float naturalW = _toastStyle.CalcSize(content).x;
        float contentW = Mathf.Min(naturalW, maxW);
        float contentH = _toastStyle.CalcHeight(content, contentW);

        float padX = toastFontSize * 0.9f;
        float padY = toastFontSize * 0.7f;
        float boxW = contentW + padX * 2f;
        float boxH = contentH + padY * 2f;
        Rect box = new Rect((Screen.width - boxW) * 0.5f, (Screen.height - boxH) * 0.5f, boxW, boxH);

        Color pc = toastPanelColor; pc.a *= a;
        DrawPanel(box, pc);

        Color tSave = _toastStyle.normal.textColor;
        Color tc = tSave; tc.a *= a;
        _toastStyle.normal.textColor = tc;
        GUI.Label(new Rect(box.x + padX, box.y + padY, contentW, contentH), content, _toastStyle);
        _toastStyle.normal.textColor = tSave;
    }

    void DrawPanel(Rect box, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(box, _panelTex);
        GUI.color = prev;
    }

    void EnsureStyles()
    {
        if (_numStyle == null)
            _numStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
        if (_capStyle == null)
            _capStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
        if (_ctrlStyle == null)
            _ctrlStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, fontStyle = FontStyle.Bold, wordWrap = false };
        if (_toastStyle == null)
            _toastStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };

        _numStyle.fontSize = fontSize;
        _numStyle.normal.textColor = textColor;
        _capStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(fontSize * 0.35f));
        _capStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
        _ctrlStyle.fontSize = controlsFontSize;
        _ctrlStyle.normal.textColor = Color.white;
        _toastStyle.fontSize = toastFontSize;
        _toastStyle.normal.textColor = Color.white;
    }

    bool LeftPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    Vector2 MousePosition()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
        return (Vector2)Input.mousePosition;
#endif
    }
}
