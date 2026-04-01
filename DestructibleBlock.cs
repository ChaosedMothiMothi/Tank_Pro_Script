using UnityEngine;

public class DestructibleBlock : MonoBehaviour
{
    [Header("設定")]
    [Tooltip("耐久値（1なら一撃で破壊）")]
    public int health = 1;

    [Tooltip("破壊時に再生するパーティクルエフェクト")]
    public GameObject breakEffect;

    // 弾(ShellController)や爆発から呼ばれるダメージ処理
    public void TakeDamage(int damage)
    {
        health -= damage;

        if (health <= 0)
        {
            BreakBlock();
        }
    }

    private void BreakBlock()
    {
        // 1. パーティクルエフェクトの生成
        if (breakEffect != null)
        {
            Instantiate(breakEffect, transform.position, transform.rotation);
        }

        // 2. ログ出力（デバッグ用）
        Debug.Log("<color=red>[Gimmick]</color> ブロックが破壊されました。");

        // 3. 本体を削除（判定も見た目も消える）
        Destroy(gameObject);
    }
}