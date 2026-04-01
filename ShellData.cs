using UnityEngine;

/// <summary>
/// 弾の基本性能を定義するScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "NewShellData", menuName = "TankGame/ShellData")]
public class ShellData : ScriptableObject
{
    public string shellName = "Normal Shell";
    public float speed = 10f;
    public int damage = 10;
    public int maxBounces = 1; // 跳弾回数
    public float lifeTime = 360f;

    [Header("爆発弾の設定")]
    public bool isExplosive;          // 爆発弾かどうか
    public int explosionDamage = 20;  // 爆発のダメージ
    public float explosionRadius = 3f; // 爆発の半径
    public float explosionDuration = 0.2f; // 判定の持続時間
    public GameObject explosionPrefab; // 演出用エフェクト
    public float explosionLifetime = 1f;   // エフェクトの消滅時間
}