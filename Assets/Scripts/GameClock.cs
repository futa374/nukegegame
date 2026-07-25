using UnityEngine;

/// <summary>
/// ゲーム内時計。1リアル秒 = timeScale ゲーム秒（デフォルト: 半日）。
/// 左下にYear/Day/時刻を表示。他スクリプトからInstance経由で参照。
/// </summary>
public class GameClock : MonoBehaviour
{
    public static GameClock Instance { get; private set; }

    [Tooltip("リアル1秒あたりのゲーム秒数。43200 = 半日/秒")]
    public float timeScale = 43200f;

    public int startYear = 2026;

    float _gameSeconds = 17716980f;

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        _gameSeconds += Time.deltaTime * timeScale;
    }

    // --- 時刻プロパティ ---
    public int Year    => startYear + (int)(_gameSeconds / (365f * 86400f));
    public int DayOfYear => (int)(_gameSeconds / 86400f) % 365 + 1;
    public int Hour    => (int)(_gameSeconds / 3600f) % 24;
    public int Minute  => (int)(_gameSeconds / 60f) % 60;

    public int Month => (DayOfYear - 1) / 30 + 1;
    public int Day   => (DayOfYear - 1) % 30 + 1;

    public string TimeString => $"{Year:0000}/{Month:00}/{Day:00} {Hour:00}:{Minute:00}";

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = 60;
        style.normal.textColor = new Color(0.85f, 0.95f, 1f);
        GUI.Label(new Rect(20, Screen.height - 80, 700, 70), TimeString, style);
    }
}
