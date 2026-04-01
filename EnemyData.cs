using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "ScriptableObjects/EnemyData")]
public class EnemyData : ScriptableObject
{
    public enum AIType
    {
        [InspectorName("ニート")] Neat,
        [InspectorName("まぬけ")] Idiot,
        [InspectorName("びびり")] Coward,
        [InspectorName("積極的")] Aggressive,
        [InspectorName("散歩好き")] Wanderer,
        [InspectorName("腰巾着")] Sycophant, 
        [InspectorName("リーダーシップ")] Leadership,
    }

    public enum TargetStrategy
    {
        [InspectorName("執着")] Persistent,   // 一度狙ったら変えない
        [InspectorName("きまぐれ")] Capricious // 3秒+ランダムで切り替え
    }

    [Header("--- AI性格設定 ---")]
    [Tooltip("敵戦車の行動パターン")]
    public AIType aiType;

    [Tooltip("ターゲット選定基準")]
    public TargetStrategy targetStrategy;

    [Header("Mine Settings")]
    public bool useMine = false;

    [Header("--- 移動・回避設定 ---")]
    [Tooltip("弾を避ける半径")]
    public float shellAvoidRadius = 3.0f;

    [Tooltip("味方地雷の認識・回避範囲（デフォルトで避ける）")]
    public float allyMineAvoidRadius = 4.0f; // ★追加

    [Tooltip("地雷を避ける半径")]
    public float mineAvoidRadius = 2.0f;

    [Tooltip("地雷設置の最低間隔")]
    public float minePlacementSpacing = 3.0f;

    [Header("--- 砲塔・攻撃設定 ---")]
    [Tooltip("砲塔回転速度")]
    public float turretRotationSpeed = 60f;

    [Tooltip("索敵時の砲塔首振り範囲")]
    public float turretSearchAngle = 15f;

    [Tooltip("射撃許容角度")]
    public float shotAllowAngle = 5f;

    [Tooltip("発射後のクールダウン（秒）")]
    public float fireCooldown = 1.0f;

    [Tooltip("味方への誤射を考慮するか")]
    public bool isTeamAware = true;

    [Tooltip("跳弾予測を考慮するか")]
    public bool considerReflection = true;

    // ★追加: 殺意の高い跳弾誘導（スマートエイム）機能
    [Header("--- デバッグ・特殊機能 ---")]
    [Tooltip("【デバッグ用】ONにすると、跳弾でプレイヤーに当たる角度を自動計算し、的確に砲塔を向けて撃つようになります（非常に強力です）")]
    public bool useSmartRicochet = false;
}