using System.Collections.Generic;
using UnityEngine;

public class OrbitingHead : MonoBehaviour
{
    public Vector3 center;
    public float radius = 3.7f;
    public Vector3 orbitAxis = Vector3.up;
    public float speedDeg = 25f;
    public float angleDeg = 0f;

    public Material hairMat;

    [System.NonSerialized] public List<GameObject> scalpHairs = new List<GameObject>();
    public bool HasHair { get { return scalpHairs.Count > 0; } }

    [System.NonSerialized] public string personName = "Unknown";
    [System.NonSerialized] public int    personAge  = 30;

    Vector3 _b1, _b2;

    readonly List<GameObject> _outlineObjects = new List<GameObject>();
    bool _outlined;

    void Start() { SetupBasis(); Place(); }

    void SetupBasis()
    {
        Vector3 a = orbitAxis.sqrMagnitude < 1e-6f ? Vector3.up : orbitAxis.normalized;
        Vector3 t = Mathf.Abs(Vector3.Dot(a, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        _b1 = Vector3.Normalize(Vector3.Cross(a, t));
        _b2 = Vector3.Normalize(Vector3.Cross(a, _b1));
    }

    void Update()
    {
        angleDeg += speedDeg * Time.deltaTime;
        Place();
    }

    void Place()
    {
        float r = angleDeg * Mathf.Deg2Rad;
        Vector3 pos = center + (_b1 * Mathf.Cos(r) + _b2 * Mathf.Sin(r)) * radius;
        Vector3 vel = (-_b1 * Mathf.Sin(r) + _b2 * Mathf.Cos(r)).normalized;
        Vector3 up = (pos - center).normalized;
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(vel, up);
    }

    public bool RemoveOneHair()
    {
        while (scalpHairs.Count > 0)
        {
            int i = scalpHairs.Count - 1;
            var g = scalpHairs[i];
            scalpHairs.RemoveAt(i);
            if (g != null) { Destroy(g); return true; }
        }
        return false;
    }

    public void ShowOutline(Material outlineMat)
    {
        if (_outlined) return;
        _outlined = true;
        var skull = transform.Find("Skull");
        if (skull == null) return;
        var mf = skull.GetComponent<MeshFilter>();
        if (mf == null) return;
        var twin = new GameObject("_Outline");
        twin.transform.SetParent(skull, false);
        twin.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var mr = twin.AddComponent<MeshRenderer>();
        mr.sharedMaterial = outlineMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        _outlineObjects.Add(twin);
    }

    public void HideOutline()
    {
        if (!_outlined) return;
        _outlined = false;
        foreach (var go in _outlineObjects)
            if (go != null) Destroy(go);
        _outlineObjects.Clear();
    }
}
