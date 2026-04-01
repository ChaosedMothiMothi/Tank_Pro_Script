using UnityEngine;
using System.Collections.Generic;

public class MapDescriptor : MonoBehaviour
{
    [Header("スポーン地点リスト")]
    [Tooltip("インデックス番号がステージデータと対応します")]
    public List<Transform> spawnPoints = new List<Transform>();

    // ギズモで場所を見やすくする
    private void OnDrawGizmos()
    {
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(spawnPoints[i].position, 0.5f);
                // UnityEditor上でのみ番号を表示（任意）
#if UNITY_EDITOR
                UnityEditor.Handles.Label(spawnPoints[i].position + Vector3.up, $"Point {i}");
#endif
            }
        }
    }
}