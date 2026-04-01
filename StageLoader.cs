using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation;


public class StageLoader : MonoBehaviour
{
    [Header("必須参照")]
    [SerializeField] private SpawnManager spawnManager;

    // ★追加: シーンに置いたNavMeshSurfaceをアタッチ
    [SerializeField] private NavMeshSurface navMeshSurface;

    [Header("デバッグ用")]
    [SerializeField] private StageData debugDefaultStage;

    private void Start()
    {
        StageData dataToLoad = GlobalGameManager.Instance != null ? GlobalGameManager.Instance.SelectedStage : null;
        if (dataToLoad == null) dataToLoad = debugDefaultStage;

        if (dataToLoad != null) LoadStage(dataToLoad);
    }

    private void LoadStage(StageData data)
    {
        // 1. マップ生成
        if (data.mapPrefab == null)
        {
            Debug.LogError("StageDataにマッププレハブが設定されていません");
            return;
        }

        GameObject mapObj = Instantiate(data.mapPrefab, Vector3.zero, Quaternion.identity);
        MapDescriptor mapDesc = mapObj.GetComponent<MapDescriptor>();

        if (mapDesc == null)
        {
            Debug.LogError("マッププレハブに MapDescriptor が付いていません");
            return;
        }

        // ★追加: NavMeshの再構築（これが重要！）
        if (navMeshSurface != null)
        {
            // 既存のデータをクリアして作り直す
            navMeshSurface.BuildNavMesh();
            Debug.Log("NavMeshを構築しました");
        }
        else
        {
            Debug.LogWarning("NavMeshSurfaceがセットされていません！");
        }


        // 2. スポーンリクエストの作成
        List<SpawnManager.SpawnRequest> requests = new List<SpawnManager.SpawnRequest>();

        foreach (var entry in data.spawnEntries)
        {
            // インデックスチェック
            if (entry.spawnPointIndex < 0 || entry.spawnPointIndex >= mapDesc.spawnPoints.Count)
            {
                Debug.LogWarning($"不正なSpawnPointIndex: {entry.spawnPointIndex}");
                continue;
            }

            Transform targetPoint = mapDesc.spawnPoints[entry.spawnPointIndex];
            if (targetPoint == null) continue;

            // 確率で戦車を決定
            GameObject selectedPrefab = SelectTankByProbability(entry.tankCandidates);

            if (selectedPrefab != null)
            {
                SpawnManager.SpawnRequest req = new SpawnManager.SpawnRequest
                {
                    prefab = selectedPrefab,
                    spawnPoint = targetPoint,
                    team = entry.team,
                    isCaptain = entry.isCaptain
                };
                requests.Add(req);
            }
        }

        // 3. 生成実行
        spawnManager.ExecuteSpawn(requests);
    }

    // 確率抽選ロジック
    private GameObject SelectTankByProbability(List<StageData.TankCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;

        // 合計確率を計算
        int totalWeight = 0;
        foreach (var c in candidates) totalWeight += c.probability;

        // 0〜合計値の間でランダムな値を決定
        int randomValue = Random.Range(0, totalWeight);

        // 抽選
        int currentWeight = 0;
        foreach (var c in candidates)
        {
            currentWeight += c.probability;
            if (randomValue < currentWeight)
            {
                return c.tankPrefab;
            }
        }

        return candidates[0].tankPrefab; // フォールバック
    }
}