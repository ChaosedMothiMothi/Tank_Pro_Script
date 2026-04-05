using UnityEngine;

// ★追加: このスクリプトをアタッチした際に、自動的に NavMeshObstacle が追加されるようにする
// （敵戦車が壁を避けるようになります）
[RequireComponent(typeof(UnityEngine.AI.NavMeshObstacle))]
public class DestructibleBlock : MonoBehaviour
{
    [Header("■ 破壊ブロック設定")]
    [Tooltip("耐久値（1なら一撃で破壊されます）")]
    [SerializeField] private int health = 1;

    [Tooltip("破壊時に再生するパーティクルエフェクトのPrefabを設定します")]
    [SerializeField] private GameObject breakEffect;

    [Tooltip("シーンビューで表示するギズモ（枠線）の色")]
    [SerializeField] private Color gizmoColor = new Color(1f, 0.5f, 0f, 0.5f);

    private void Awake()
    {
        // ★追加: ゲーム開始時に NavMeshObstacle の設定を自動で最適化する
        var obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            // 動的にNavMeshをくり抜く（敵がここを避けて経路探索するようになる）
            obstacle.carving = true;
        }
    }

    [Tooltip("弾や爆発から呼び出されるダメージ処理")]
    public void TakeDamage(int damage)
    {
        health -= damage;

        if (health <= 0)
        {
            BreakBlock();
        }
    }

    [Tooltip("ブロックの破壊処理とエフェクトの生成")]
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

    [Tooltip("Unityエディターのシーンビューに当たり判定の枠を描画する処理")]
    private void OnDrawGizmos()
    {
        // BoxCollider のサイズに合わせてギズモを描画する
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            // ギズモの色を設定
            Gizmos.color = gizmoColor;

            // オブジェクトの回転とスケールを考慮してマトリックスを設定
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

            // BoxColliderの中心とサイズに合わせて半透明のキューブを描画
            Gizmos.DrawCube(box.center, box.size);

            // 枠線（ワイヤーフレーム）も描画して見やすくする
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}