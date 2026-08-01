using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class HairHoverController : MonoBehaviour
{
    public Color outlineColor = new Color(0.2f, 0.8f, 1f);
    public float outlineWidth = 0.008f;

    [Header("追随対象のハイライト")]
    [Tooltip("追随（選択）中の対象をどれだけ明るくするか。1で無変化、1.5で1.5倍。")]
    [Range(1f, 3f)] public float followBrightness = 1.3f;
    [Tooltip("毛を選択したときの発光の強さ。0で発光なし。毛のように暗く細いものは発光の方が視認しやすい。")]
    [Range(0f, 4f)] public float followEmission = 1.2f;
    [Tooltip("頭を選択したときの発光の強さ。頭は肌が明るいので弱め。表面テクスチャが飛ばない程度に。")]
    [Range(0f, 4f)] public float headEmission = 0.25f;
    [Tooltip("発光の色みを元の色からどれだけ白へ寄せるか。暗い毛でも光って見えるように少し白へ寄せる。")]
    [Range(0f, 1f)] public float followGlowWhiteness = 0.6f;

    Camera _cam;
    PlanetCameraRig _rig;
    Material _outlineMat;

    // ホバー中
    PlanetHair    _hoveredHair;
    OrbitingHead  _hoveredHead;

    // 選択中（フォーカス中）
    PlanetHair    _selectedHair;
    OrbitingHead  _selectedHead;

    void Start()
    {
        _cam = GetComponent<Camera>();
        _rig = GetComponent<PlanetCameraRig>();

        var shader = Shader.Find("Custom/HairOutline");
        _outlineMat = new Material(shader);
        _outlineMat.SetColor("_OutlineColor", outlineColor);
        _outlineMat.SetFloat("_OutlineWidth", outlineWidth);
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        // ESC or 右クリック: フォーカス解除
        if (Keyboard.current.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            Deselect();

        // ドラッグ中はホバー判定スキップ
        if (mouse.leftButton.isPressed && mouse.delta.ReadValue().sqrMagnitude > 1f) return;

        // レイキャスト
        Vector2 mousePos = mouse.position.ReadValue();
        Ray ray = _cam.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0f));

        PlanetHair   hitHair = null;
        OrbitingHead hitHead = null;
        if (Physics.Raycast(ray, out RaycastHit hitInfo))
        {
            hitHair = hitInfo.collider.GetComponentInParent<PlanetHair>();
            if (hitHair == null)
                hitHead = hitInfo.collider.GetComponentInParent<OrbitingHead>();
        }

        // ホバー更新（毛）
        if (hitHair != _hoveredHair)
        {
            if (_hoveredHair != null && _hoveredHair != _selectedHair) _hoveredHair.HideOutline();
            _hoveredHair = hitHair;
            if (_hoveredHair != null && _hoveredHair != _selectedHair) _hoveredHair.ShowOutline(_outlineMat);
        }

        // ホバー更新（頭）
        if (hitHead != _hoveredHead)
        {
            if (_hoveredHead != null && _hoveredHead != _selectedHead) _hoveredHead.HideOutline();
            _hoveredHead = hitHead;
            if (_hoveredHead != null && _hoveredHead != _selectedHead) _hoveredHead.ShowOutline(_outlineMat);
        }

        // クリック
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if      (_hoveredHair != null) SelectHair(_hoveredHair);
            else if (_hoveredHead != null) SelectHead(_hoveredHead);
            else                           Deselect();
        }
    }

    void SelectHair(PlanetHair hair)
    {
        Deselect();
        _selectedHair = hair;
        ApplyHighlight(_selectedHair.transform, followEmission);   // アウトライン複製より先に、素の見た目へ適用
        _selectedHair.ShowOutline(_outlineMat);
        _rig?.FollowHair(_selectedHair.transform);
    }

    void SelectHead(OrbitingHead head)
    {
        Deselect();
        _selectedHead = head;
        ApplyHighlight(_selectedHead.transform, headEmission);   // 頭は弱めの発光でテクスチャを残す
        _selectedHead.ShowOutline(_outlineMat);
        _rig?.FollowHair(_selectedHead.transform);
    }

    void Deselect()
    {
        ClearHighlight();
        if (_selectedHair != null) { _selectedHair.HideOutline(); _selectedHair = null; }
        if (_selectedHead != null) { _selectedHead.HideOutline(); _selectedHead = null; }
        _rig?.ExitFollow();
    }

    // ── 追随対象を少し明るく／発光させる ──────────────────────
    // 頭も毛も、同じ種類同士でマテリアルを共有している。共有マテリアルを
    // 直接いじると兄弟まで光ってしまうので、対象の Renderer のマテリアルだけ
    // 複製し、複製側でベースカラーを持ち上げ・エミッションを有効化する。
    // 解除時に元のマテリアルへ戻し、作った複製は破棄する。
    // （エミッションは MaterialPropertyBlock では出ない＝_EMISSION キーワードが
    //   マテリアル単位で必要なため、複製方式にしている）
    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    static readonly int ColorID     = Shader.PropertyToID("_Color");
    static readonly int EmissionID  = Shader.PropertyToID("_EmissionColor");

    readonly List<Renderer> _hlScratch = new List<Renderer>();

    struct HighlightEntry { public Renderer renderer; public Material[] original; public Material[] instances; }
    readonly List<HighlightEntry> _highlights = new List<HighlightEntry>();

    void ApplyHighlight(Transform target, float emission)
    {
        ClearHighlight();
        if (target == null) return;
        bool brighten = followBrightness > 1.001f;
        bool glow     = emission         > 0.001f;
        if (!brighten && !glow) return;

        target.GetComponentsInChildren(true, _hlScratch);
        foreach (var r in _hlScratch)
        {
            var orig = r.sharedMaterials;
            var inst = new Material[orig.Length];
            bool touched = false;
            for (int i = 0; i < orig.Length; i++)
            {
                var mat = orig[i];
                if (mat == null || mat == _outlineMat) { inst[i] = mat; continue; } // アウトライン複製は素通し

                var m = new Material(mat);   // この Renderer 専用の複製

                Color baseC = mat.HasProperty(BaseColorID) ? mat.GetColor(BaseColorID)
                            : mat.HasProperty(ColorID)     ? mat.GetColor(ColorID)
                            :                                Color.white;

                if (brighten)
                {
                    Color bright = baseC * followBrightness; bright.a = baseC.a;
                    if (m.HasProperty(BaseColorID)) m.SetColor(BaseColorID, bright);
                    if (m.HasProperty(ColorID))     m.SetColor(ColorID, bright);
                }

                if (glow)
                {
                    // 元の色を少し白へ寄せた色で発光させる（暗い毛でも光って見えるように）
                    Color glowColor = Color.Lerp(baseC, Color.white, followGlowWhiteness) * emission;
                    glowColor.a = 1f;
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    if (m.HasProperty(EmissionID)) m.SetColor(EmissionID, glowColor);
                }

                inst[i] = m;
                touched = true;
            }

            if (touched)
            {
                r.sharedMaterials = inst;
                _highlights.Add(new HighlightEntry { renderer = r, original = orig, instances = inst });
            }
        }
    }

    void ClearHighlight()
    {
        foreach (var e in _highlights)
        {
            if (e.renderer != null) e.renderer.sharedMaterials = e.original;  // 元のマテリアルへ戻す
            for (int i = 0; i < e.instances.Length; i++)
            {
                var m = e.instances[i];
                if (m != null && m != e.original[i]) Destroy(m);              // 作った複製だけ破棄
            }
        }
        _highlights.Clear();
    }

    void OnDisable() { ClearHighlight(); }

    void OnGUI()
    {
        if (_selectedHead == null && _selectedHair == null) return;

        string title, sub;
        if (_selectedHead != null)
        {
            title = _selectedHead.personName;
            sub   = $"Age  {_selectedHead.personAge}";
        }
        else
        {
            title = _selectedHair.ownerName + " の毛";
            sub   = "Born  " + _selectedHair.birthTimeString;
        }

        var titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize  = 88;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = Color.white;
        titleStyle.alignment = TextAnchor.UpperRight;

        var subStyle = new GUIStyle(GUI.skin.label);
        subStyle.fontSize = 60;
        subStyle.normal.textColor = new Color(0.75f, 0.9f, 1f);
        subStyle.alignment = TextAnchor.UpperRight;

        float w = 900f, x = Screen.width - w - 20f;
        GUI.Label(new Rect(x, 20f, w, 100f), title, titleStyle);
        GUI.Label(new Rect(x, 115f, w, 80f), sub,   subStyle);
    }
}
