using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "ScriptableObjects/EnemyData")]
public class EnemyData : ScriptableObject
{
    public enum AIType
    {
        [InspectorName("ニート")] Neat,
        [InspectorName("まぬけ")] Idiot,
        [InspectorName("おくびょう")] Coward,
        [InspectorName("愚直")] Aggressive,
        [InspectorName("散歩好き")] Wanderer,
        [InspectorName("腰巾着")] Sycophant,
        [InspectorName("リーダー")] Leadership,
    }

    public enum TargetStrategy
    {
        [InspectorName("執着")] Persistent,
        [InspectorName("気まぐれ")] Capricious
    }

    [Header("--- AI ---")]
    [Tooltip("性格")]
    public AIType aiType;

    [Tooltip("狙い方")]
    public TargetStrategy targetStrategy;

    [Header("--- エネミー設定 ---")]
    [Tooltip("パーツドロップ数")]
    public int partsDropCount = 1;

    [Tooltip("ボスのパーツドロップ")]
    public bool isBossDrop = false;

    [Header("--- 地雷の設定 ---")]
    [Tooltip("地雷を使用するか")]
    public bool useMine = false;

    [Tooltip("地雷の再配置間隔距離")]
    public float minePlacementSpacing = 3.0f;

    [Header("--- 回避性能 ---")]
    [Tooltip("弾の回避半径")]
    public float shellAvoidRadius = 3.0f;

    [Tooltip("味方地雷の回避半径")]
    public float allyMineAvoidRadius = 4.0f;

    [Tooltip("地雷の回避半径")]
    public float mineAvoidRadius = 2.0f;

    [Header("--- 射撃設定 ---")]
    [Tooltip("砲塔回転速度")]
    public float turretRotationSpeed = 60f;

    [Tooltip("砲塔の索敵角度")]
    public float turretSearchAngle = 15f;

    [Tooltip("射線許容角度")]
    public float shotAllowAngle = 5f;

    [Tooltip("射撃後のクールタイム")]
    public float fireCooldown = 1.0f;

    [Tooltip("連射タイプか")]
    public bool isGatlingType = false;

    [Header("--- 射線意識について ---")]
    [Tooltip("味方意識を持つか")]
    public bool isTeamAware = true;

    [Tooltip("跳弾を意識するか")]
    public bool considerReflection = true;

    [Tooltip("跳弾意識強化")]
    public bool useSmartRicochet = false;

    [Tooltip("射線の太さ")]
    public float raycastRadius = 0.3f;
}
