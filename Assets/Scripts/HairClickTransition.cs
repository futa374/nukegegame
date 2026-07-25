using UnityEngine;
using UnityEngine.InputSystem;

public class HairClickTransition : MonoBehaviour
{
    public string hairTag = "hair";
    public string targetScene = "ScalpScene";
    public float screenRadiusThreshold = 150f; // 画面中央からのpx距離

    private Camera cam;
    private bool hairInSight;
    private Transform activeLocator;
    private Transform[] locators;

    void Start()
    {
        cam = GetComponent<Camera>();
        var hairs = GameObject.FindGameObjectsWithTag(hairTag);
        locators = new Transform[hairs.Length];
        for (int i = 0; i < hairs.Length; i++)
            locators[i] = hairs[i].transform.Find("iconlocator");
    }

    void Update()
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        hairInSight  = false;
        activeLocator = null;

        float bestDist = float.MaxValue;
        for (int i = 0; i < locators.Length; i++)
        {
            if (locators[i] == null) continue;

            Vector3 sp = cam.WorldToScreenPoint(locators[i].position);
            if (sp.z < 0) continue;

            // 画面中央からのスクリーン距離
            float d = Vector2.Distance(new Vector2(sp.x, sp.y), screenCenter);
            if (d < screenRadiusThreshold && d < bestDist)
            {
                bestDist = d;
                hairInSight = true;
                activeLocator = locators[i];
            }
        }

        // クリック: ◎が出てる髪のHairSceneTargetから遷移先を取得
        if (Mouse.current.leftButton.wasPressedThisFrame && hairInSight && activeLocator != null)
        {
            var target = activeLocator.GetComponentInParent<HairSceneTarget>();
            string scene = target != null ? target.targetScene : targetScene;
            if (!string.IsNullOrEmpty(scene))
                SceneTransitioner.Get().TransitionTo(scene);
        }
    }

    void OnGUI()
    {
        if (!hairInSight || activeLocator == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(activeLocator.position);
        if (screenPos.z < 0) return;

        float scale = Mathf.Abs(Mathf.Sin(Time.time * 2.5f));
        int fontSize = Mathf.RoundToInt(140f * scale);
        if (fontSize < 1) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = fontSize;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.black;
        style.alignment = TextAnchor.MiddleCenter;

        float w = 200f, h = 200f;
        float guiY = Screen.height - screenPos.y;
        GUI.Label(new Rect(screenPos.x - w * 0.5f, guiY - h * 0.5f, w, h), "◎", style);
    }
}
