using UnityEngine;

/// <summary>
/// スポーン場所を識別するためのコンポーネント。
/// シーン上に配置した空のGameObjectにアタッチします。
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    // 将来的に「このポイントからは敵しか出さない」などの
    // 個別設定が必要になった場合、ここに変数を追加できます。

    private void OnDrawGizmos()
    {
        // エディタ上で場所がわかりやすいようにアイコンを表示
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward);
    }
}