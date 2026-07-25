using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 天候切り替え × 抜け毛ダメージ表現（プロトタイプ / 単体完結スクリプト）。
///
/// 既存スクリプト（CameraOrbit / HairClickLogger 等）とのコンフリクトを避けるため、
/// このファイル単体で完結するように新規作成。空の GameObject に 1 つアタッチするだけで動作し、
/// 対象（ライト・髪・カメラ）は自動検出する（Inspector で個別に上書き可能）。
///
/// 天候：
///   晴れ(Sunny)   … 陽射しが強くなり、頭皮（顔メッシュ）が徐々に日焼けして赤みを帯びる
///   雨(Rain)      … 画面全体に雨の描写 ＋ 髪が濡れて重く垂れ下がり、揺れが小さくなる
///   台風(Typhoon) … 強風の描写（雨より激しい）＋ 髪が大きく激しくたなびく
///   通常(Clear)   … 上記いずれも無し（穏やかな待機揺れのみ）
///
/// テスト用に 0/1/2/3 キーで天候を切り替え可能（Clear/Sunny/Rain/Typhoon）。
/// 他スクリプトからは SetWeather(WeatherType) を呼ぶだけで連携できる。
/// </summary>
public class Script2 : MonoBehaviour
{
    public enum WeatherType { Clear, Sunny, Rain, Typhoon }

    [Header("天候")]
    public WeatherType currentWeather = WeatherType.Clear;
    [Tooltip("0:通常 1:晴れ 2:雨 3:台風 キーでテスト切り替え")]
    public bool enableKeyboardShortcuts = true;
    [Tooltip("天候変化のブレンド速度（秒あたりの遷移割合）")]
    public float transitionSpeed = 0.8f;

    [Header("対象の自動検出（未設定なら自動で探す）")]
    public Light sunLight;                 // Directional Light
    public Transform hairTarget;           // 揺らす対象（未設定なら tag "hair" → "face" → "Hairtest" の順で検索）
    public Renderer scalpRenderer;         // 日焼け表現に使うレンダラー（未設定なら hairTarget から取得）
    public Camera targetCamera;            // 雨・強風パーティクルを追従させるカメラ（未設定なら Camera.main）

    [Header("揺れ（共通ベース）")]
    public float baseSwayAmplitudeDeg = 3f;
    public float baseSwayFrequency = 0.6f;

    [Header("晴れ：日焼け")]
    public float sunnyIntensityMultiplier = 1.6f;
    public Color sunnyLightColor = new Color(1f, 0.95f, 0.82f);
    public Color sunburnColor = new Color(0.78f, 0.24f, 0.18f);
    [Tooltip("日焼けが最大に達するまでの秒数")]
    public float sunburnBuildSeconds = 10f;
    [Tooltip("晴れていない時に日焼けが引く速さ（秒あたり）")]
    public float sunburnFadeSpeed = 0.05f;

    [Header("雨：濡れ髪")]
    public Color rainLightColor = new Color(0.62f, 0.68f, 0.78f);
    public float rainIntensityMultiplier = 0.5f;
    [Range(0f, 60f)] public float wetDroopAngleDeg = 22f;   // 濡れて頭が下がる/垂れる角度
    [Range(0f, 1f)] public float wetSwayDamp = 0.2f;        // 濡れている間の揺れ減衰係数
    public int rainParticleCount = 400;
    public float rainAreaSize = 6f;
    public float rainFallSpeed = 9f;

    [Header("台風：強風")]
    public Color typhoonLightColor = new Color(0.5f, 0.55f, 0.55f);
    public float typhoonIntensityMultiplier = 0.55f;
    public float typhoonSwayAmplitudeDeg = 26f;
    public float typhoonSwayFrequency = 3.2f;
    public int windParticleCount = 250;
    public float windFallSpeed = 14f;

    // ---- 内部状態 ----
    float _baseLightIntensity = 1f;
    Color _baseLightColor = Color.white;
    Quaternion _hairBaseLocalRotation = Quaternion.identity;
    Color _baseScalpColor = Color.white;
    string _scalpColorProperty = "_BaseColor";

    float _sunburnT;   // 0-1 日焼け進行度
    float _wetT;       // 0-1 濡れ具合（雨の間に上昇し、止むと下降）
    float _windT;      // 0-1 台風の風（切替と共に速く追従）
    float _noiseSeedA, _noiseSeedB;
    bool _scalpTinted; // 日焼け色を上書き中かどうか（元の肌色に戻す判定用）

    ParticleSystem _rainParticles;
    ParticleSystem _windParticles;
    MaterialPropertyBlock _mpb;

    void Awake()
    {
        _noiseSeedA = Random.Range(0f, 1000f);
        _noiseSeedB = Random.Range(0f, 1000f);

        AutoDetectTargets();
        CacheBaseValues();

        _rainParticles = BuildWeatherParticles("Script2_RainFX", rainParticleCount, isWind: false);
        _windParticles = BuildWeatherParticles("Script2_WindFX", windParticleCount, isWind: true);

        ApplyImmediate(currentWeather);
    }

    void AutoDetectTargets()
    {
        if (sunLight == null)
        {
            var lightObj = GameObject.Find("Directional Light");
            sunLight = lightObj != null ? lightObj.GetComponent<Light>() : FindAnyObjectByType<Light>();
        }

        if (hairTarget == null)
        {
            var hairObjs = GameObject.FindGameObjectsWithTag("hair");
            if (hairObjs != null && hairObjs.Length > 0)
                hairTarget = hairObjs[0].transform;
        }
        if (hairTarget == null)
        {
            var go = GameObject.Find("face") ?? GameObject.Find("Hairtest");
            if (go != null) hairTarget = go.transform;
        }

        if (scalpRenderer == null && hairTarget != null)
            scalpRenderer = hairTarget.GetComponentInChildren<Renderer>();

        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    void CacheBaseValues()
    {
        if (sunLight != null)
        {
            _baseLightIntensity = sunLight.intensity;
            _baseLightColor = sunLight.color;
        }
        if (hairTarget != null)
        {
            _hairBaseLocalRotation = hairTarget.localRotation;
        }
        if (scalpRenderer != null && scalpRenderer.sharedMaterial != null)
        {
            _mpb = new MaterialPropertyBlock();
            var mat = scalpRenderer.sharedMaterial;
            if (mat.HasProperty("_BaseColor")) _scalpColorProperty = "_BaseColor";
            else if (mat.HasProperty("_Color")) _scalpColorProperty = "_Color";
            _baseScalpColor = mat.HasProperty(_scalpColorProperty) ? mat.GetColor(_scalpColorProperty) : Color.white;
        }
    }

    /// <summary>他スクリプトから天候を切り替えるための公開API。</summary>
    public void SetWeather(WeatherType type)
    {
        currentWeather = type;
    }

    void Update()
    {
        if (enableKeyboardShortcuts) HandleKeyboardShortcuts();

        float dt = Time.deltaTime;

        bool isRain = currentWeather == WeatherType.Rain;
        bool isSunny = currentWeather == WeatherType.Sunny;
        bool isTyphoon = currentWeather == WeatherType.Typhoon;

        _wetT = Mathf.MoveTowards(_wetT, isRain ? 1f : 0f, dt * transitionSpeed);
        _windT = Mathf.MoveTowards(_windT, isTyphoon ? 1f : 0f, dt * (transitionSpeed * 1.5f));
        _sunburnT = isSunny
            ? Mathf.Min(1f, _sunburnT + dt / Mathf.Max(0.01f, sunburnBuildSeconds))
            : Mathf.Max(0f, _sunburnT - dt * sunburnFadeSpeed);

        UpdateLight(dt);
        UpdateHairSway();
        UpdateScalpSunburn();
        UpdateParticles(isRain, isTyphoon);
    }

    void HandleKeyboardShortcuts()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit0Key.wasPressedThisFrame || kb.numpad0Key.wasPressedThisFrame) SetWeather(WeatherType.Clear);
        else if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) SetWeather(WeatherType.Sunny);
        else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) SetWeather(WeatherType.Rain);
        else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) SetWeather(WeatherType.Typhoon);
#else
        if (Input.GetKeyDown(KeyCode.Alpha0)) SetWeather(WeatherType.Clear);
        else if (Input.GetKeyDown(KeyCode.Alpha1)) SetWeather(WeatherType.Sunny);
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SetWeather(WeatherType.Rain);
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SetWeather(WeatherType.Typhoon);
#endif
    }

    void UpdateLight(float dt)
    {
        if (sunLight == null) return;

        float targetIntensity = _baseLightIntensity;
        Color targetColor = _baseLightColor;

        if (currentWeather == WeatherType.Sunny)
        {
            targetIntensity = _baseLightIntensity * sunnyIntensityMultiplier;
            targetColor = sunnyLightColor;
        }
        else if (currentWeather == WeatherType.Rain)
        {
            targetIntensity = _baseLightIntensity * rainIntensityMultiplier;
            targetColor = rainLightColor;
        }
        else if (currentWeather == WeatherType.Typhoon)
        {
            targetIntensity = _baseLightIntensity * typhoonIntensityMultiplier;
            targetColor = typhoonLightColor;
        }

        float t = dt * transitionSpeed;
        sunLight.intensity = Mathf.Lerp(sunLight.intensity, targetIntensity, t);
        sunLight.color = Color.Lerp(sunLight.color, targetColor, t);
    }

    void UpdateHairSway()
    {
        if (hairTarget == null) return;

        float time = Time.time;

        // 台風ほど速く・大きく、雨ほど濡れて重く（揺れが小さく、下に垂れる）
        float amp = Mathf.Lerp(baseSwayAmplitudeDeg, typhoonSwayAmplitudeDeg, _windT);
        amp = Mathf.Lerp(amp, amp * wetSwayDamp, _wetT);

        float freq = Mathf.Lerp(baseSwayFrequency, typhoonSwayFrequency, _windT);

        float noiseX = (Mathf.PerlinNoise(_noiseSeedA, time * freq) - 0.5f) * 2f;
        float noiseZ = (Mathf.PerlinNoise(_noiseSeedB, time * freq * 0.8f) - 0.5f) * 2f;

        float swayX = noiseX * amp;
        float swayZ = noiseZ * amp;
        float droopX = wetDroopAngleDeg * _wetT; // 濡れてうなだれる分（前方向に傾く）

        Quaternion swayRot = Quaternion.Euler(swayX + droopX, 0f, swayZ);
        hairTarget.localRotation = _hairBaseLocalRotation * swayRot;
    }

    void UpdateScalpSunburn()
    {
        if (scalpRenderer == null) return;

        // 日焼けしていない間は一切上書きしない＝常に元の肌色（マテリアル本来の見た目）のまま。
        if (_sunburnT <= 0.0001f)
        {
            if (_scalpTinted)
            {
                scalpRenderer.SetPropertyBlock(null); // 上書きを完全に解除して元の見た目へ戻す
                _scalpTinted = false;
            }
            return;
        }

        if (_mpb == null) _mpb = new MaterialPropertyBlock();
        // 「無変化＝白（乗算1倍）」を基準に日焼け色へブレンドする。
        // 元の _BaseColor を保持・復元する必要がなく、誤って地の色を壊すことがない。
        Color c = Color.Lerp(Color.white, sunburnColor, _sunburnT);
        scalpRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_scalpColorProperty, c);
        scalpRenderer.SetPropertyBlock(_mpb);
        _scalpTinted = true;
    }

    void UpdateParticles(bool isRain, bool isTyphoon)
    {
        if (targetCamera != null)
        {
            Vector3 followPos = targetCamera.transform.position + targetCamera.transform.forward * (rainAreaSize * 0.4f);
            if (_rainParticles != null) _rainParticles.transform.position = followPos + Vector3.up * (rainAreaSize * 0.5f);
            if (_windParticles != null) _windParticles.transform.position = followPos + Vector3.up * (rainAreaSize * 0.3f);
        }

        SetEmission(_rainParticles, isRain);
        SetEmission(_windParticles, isTyphoon);
    }

    void SetEmission(ParticleSystem ps, bool active)
    {
        if (ps == null) return;
        var emission = ps.emission;
        if (active && !ps.isPlaying) ps.Play();
        emission.enabled = active;
        if (!active && ps.isPlaying && ps.particleCount == 0) ps.Stop();
    }

    void ApplyImmediate(WeatherType type)
    {
        _wetT = type == WeatherType.Rain ? 1f : 0f;
        _windT = type == WeatherType.Typhoon ? 1f : 0f;
        _sunburnT = type == WeatherType.Sunny ? 1f : 0f;
        UpdateHairSway();
        UpdateScalpSunburn();
        if (sunLight != null)
        {
            if (type == WeatherType.Sunny) { sunLight.intensity = _baseLightIntensity * sunnyIntensityMultiplier; sunLight.color = sunnyLightColor; }
            else if (type == WeatherType.Rain) { sunLight.intensity = _baseLightIntensity * rainIntensityMultiplier; sunLight.color = rainLightColor; }
            else if (type == WeatherType.Typhoon) { sunLight.intensity = _baseLightIntensity * typhoonIntensityMultiplier; sunLight.color = typhoonLightColor; }
            else { sunLight.intensity = _baseLightIntensity; sunLight.color = _baseLightColor; }
        }
    }

    /// <summary>
    /// 雨/強風の飛沫を表現する簡易パーティクルをコードのみで生成（追加アセット不要）。
    /// isWind=true の場合はより速く・斜めに荒く飛ぶ「台風の飛散物」寄りの設定にする。
    /// </summary>
    ParticleSystem BuildWeatherParticles(string name, int count, bool isWind)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.maxParticles = Mathf.Max(count, 16);
        main.startLifetime = isWind ? 1.2f : 1.6f;
        main.startSpeed = isWind ? windFallSpeed : rainFallSpeed;
        main.startSize = isWind ? new ParticleSystem.MinMaxCurve(0.01f, 0.03f) : new ParticleSystem.MinMaxCurve(0.01f, 0.02f);
        main.startColor = isWind ? new Color(0.55f, 0.5f, 0.4f, 0.5f) : new Color(0.75f, 0.85f, 1f, 0.55f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = isWind ? 0.15f : 1f;

        var emission = ps.emission;
        emission.rateOverTime = count;
        emission.enabled = false;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(rainAreaSize, 0.2f, rainAreaSize);

        if (isWind)
        {
            // 台風：ほぼ水平に強く流れる荒れた風の飛散物
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(windFallSpeed * 0.6f, windFallSpeed * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            var rot = ps.transform;
            rot.localRotation = Quaternion.Euler(0f, 0f, 80f); // 横殴りの向き
        }
        else
        {
            main.startRotation3D = false;
        }

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = isWind ? 0.05f : 0.12f;
        renderer.lengthScale = isWind ? 2f : 4f;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var mat = new Material(shader);
            renderer.material = mat;
        }

        return ps;
    }

    void OnGUI()
    {
        if (!enableKeyboardShortcuts) return;
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 18 };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(20, 20, 500, 30), "天候切替　0:通常 / 1:晴れ / 2:雨 / 3:台風  （現在: " + currentWeather + "）", style);
    }
}
