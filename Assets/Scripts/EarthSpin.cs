using UnityEngine;

/// <summary>
/// 地球（Earth メッシュ）をその場で自転させるだけの独立スクリプト。
///
/// realearth シーンの地球は実モデルで、自転を駆動するものが無かった。
/// PlanetController / OrbitingHead など既存の実装には一切触れず、
/// このコンポーネントを Earth オブジェクトに付けるだけで自転する。
///
/// ・axis         … 回転軸。既定は上下（ワールドの縦軸）まわりの地球らしい自転。
/// ・degreesPerSecond … 1秒あたりの回転角。プラスで反時計回り、マイナスで逆回り。
/// ・space        … World なら見た目の縦軸で素直に回る。Local ならモデルの傾きに沿う。
///
/// すべて Time.deltaTime で回すので、Time.timeScale の影響を受ける
/// （＝ポーズや低速再生にもそのまま追従する）。
/// </summary>
public class EarthSpin : MonoBehaviour
{
    [Tooltip("回転軸。既定は縦軸まわり（地球の自転らしい向き）。")]
    public Vector3 axis = Vector3.up;

    [Tooltip("1秒あたりの回転角（度）。マイナスで逆回転。")]
    public float degreesPerSecond = 5f;

    [Tooltip("World: 見た目の軸で回す / Self: モデルの傾きに沿って回す。")]
    public Space space = Space.World;

    void Update()
    {
        Vector3 a = axis.sqrMagnitude < 1e-6f ? Vector3.up : axis.normalized;
        transform.Rotate(a, degreesPerSecond * Time.deltaTime, space);
    }
}
