using UnityEngine;

/// <summary>
/// 地雷の性能を定義するScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "NewMineData", menuName = "TankGame/MineData")]
public class MineData : ScriptableObject
{
    public string mineName = "Anti-Tank Mine";
    public float explosionDelay = 5.0f;     // 自動爆発までの時間
    public int damage = 30;                 // 爆発ダメージ
    public float explosionRadius = 3.0f;    // 爆発半径
    public float explosionDuration = 0.2f;  // 爆発判定の持続時間
    public GameObject effectPrefab;         // 爆発エフェクト
    public float effectDuration = 2.0f;     // エフェクトの残存時間
}