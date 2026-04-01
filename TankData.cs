using UnityEngine;

/// <summary>
/// 戦車の基本ステータスを定義するScriptableObject
/// 拡張性を考慮し、パラメータの追加が容易な構造にしています。
/// </summary>
[CreateAssetMenu(fileName = "NewTankStatus", menuName = "TankGame/TankStatus")]
public class TankStatusData : ScriptableObject
{
    [Header("基本設定")]
    [Tooltip("戦車の名称")]
    public string tankName = "Default Tank";

    [Header("耐久・移動")]
    [Tooltip("最大体力")]
    public int maxHp = 100;

    [Tooltip("移動速度 (m/s)")]
    public float moveSpeed = 5.0f;

    [Tooltip("車体の回転速度 (度/秒)")]
    public float rotationSpeed = 90.0f;

    [Header("攻撃（通常弾）")]
    [Tooltip("弾の最大所持数")]
    public int maxAmmo = 10;

    [Tooltip("弾の次弾装填までのクールタイム（秒）")]
    public float ammoCooldown = 1.5f;

    [Tooltip("弾を発射した後の硬直時間（秒）")]
    public float shotDelay = 0.5f;

    [Header("特殊装備（地雷）")]
    [Tooltip("地雷の最大所持数")]
    public int maxMines = 3;

    [Tooltip("地雷の再設置可能になるまでのクールタイム（秒）")]
    public float mineCooldown = 10.0f;

    [Tooltip("地雷を設置する際の硬直時間（秒）")]
    public float minePlacementDelay = 1.0f;

    [Header("敵専用データ")]

    [Header("Initial Equipment")]
    [Tooltip("初期装備として持たせるシールドデータ。なしなら空欄")]
    public ShieldData startingShield;

    [Header("Self Destruct Settings")]
    [Tooltip("この戦車は死亡時に自爆するか")]
    public bool isSelfDestruct = false;

    [Tooltip("死亡から爆発までの猶予時間")]
    public float selfDestructInterval = 15f;

    [Tooltip("爆発ダメージ")]
    public int selfDestructDamage = 20;

    [Tooltip("爆発半径")]
    public float selfDestructRadius = 6.0f;

    [Tooltip("爆発エフェクトの表示時間（判定時間ではない）")]
    public float explosionEffectDuration = 2.0f;

    [Tooltip("自爆用の爆発プレハブ")]
    public GameObject selfDestructExplosionPrefab;


}