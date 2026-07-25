using UnityEngine;
using UnityEngine.InputSystem;

public class HairHoverController : MonoBehaviour
{
    public Color outlineColor = new Color(0.2f, 0.8f, 1f);
    public float outlineWidth = 0.008f;

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
        _selectedHair.ShowOutline(_outlineMat);
        _rig?.FollowHair(_selectedHair.transform);
    }

    void SelectHead(OrbitingHead head)
    {
        Deselect();
        _selectedHead = head;
        _selectedHead.ShowOutline(_outlineMat);
        _rig?.FollowHair(_selectedHead.transform);
    }

    void Deselect()
    {
        if (_selectedHair != null) { _selectedHair.HideOutline(); _selectedHair = null; }
        if (_selectedHead != null) { _selectedHead.HideOutline(); _selectedHead = null; }
        _rig?.ExitFollow();
    }

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
