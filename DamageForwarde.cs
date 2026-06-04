using UnityEngine;

/// <summary>
/// 牽引ボスの「大型個体」など、TankStatusを���たない別オブジェクトにアタッチし、
/// 受けたダメージを本体の TankStatus に転送するためのコンポーネント。
/// </summary>
public class DamageForwarder : MonoBehaviour
{
    [Tooltip("ダメージを転送する本体のTankStatus")]
    public TankStatus mainTankStatus;

    // 地雷や爆風などの GetComponentsInParent<TankStatus>() で本体を見つけられるように、
    // GetComponentInParent が呼ばれた際に自身の mainTankStatus を返す役割を果たしたいが、
    // Unityの仕様上コンポーネントを偽装することはできない。
    // そのため、呼び出し側（ShellController や MineController）が
    // DamageForwarder を検知した場合は本体へルーティングするようにする。
}