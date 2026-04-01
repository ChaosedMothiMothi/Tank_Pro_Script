using UnityEngine;

public class SpikeBlock : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("ゲーム開始時に見た目を消すかどうか")]
    [SerializeField] private bool hideInGame = true;

    private void Awake()
    {
        // レイヤーを強制的に設定（設定ミス防止）
        // ※事前にUnityのTags & Layersで "Spike" レイヤーを作成してください
        int spikeLayer = LayerMask.NameToLayer("Spike");
        if (spikeLayer != -1)
        {
            gameObject.layer = spikeLayer;
        }
        else
        {
            Debug.LogError("Layer 'Spike' が存在しません！Unityエディタで作成してください。");
        }

        // ゲーム中は見た目を消す（当たり判定は残る）
        if (hideInGame)
        {
            Renderer r = GetComponent<Renderer>();
            if (r != null) r.enabled = false;
        }
    }

    // エディタ上で配置を確認するための描画
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.5f); // 赤半透明

        // 自身のコライダーに合わせて描画
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else
        {
            // コライダーがなければTransformで簡易描画
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
        }
    }
}