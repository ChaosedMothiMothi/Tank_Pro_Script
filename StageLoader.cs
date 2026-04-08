using UnityEngine;
using System.Collections.Generic;
using Unity.AI.Navigation;

public class StageLoader : MonoBehaviour
{
    [Header("必須参照")]
    [SerializeField] private SpawnManager spawnManager;
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

        if (navMeshSurface != null)
        {
            navMeshSurface.BuildNavMesh();
            Debug.Log("NavMeshを構築しました");
        }

        List<SpawnManager.SpawnRequest> requests = new List<SpawnManager.SpawnRequest>();

        // ★撃破保存用の通し番号
        int uniqueSpawnId = 0;

        // 1. 戦車の生成リクエスト
        if (data.spawnEntries != null)
        {
            foreach (var entry in data.spawnEntries)
            {
                int currentId = uniqueSpawnId++;

                // 既に倒されていればスキップ
                if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.IsDefeated(currentId)) continue;
                if (entry.spawnPointIndex < 0 || entry.spawnPointIndex >= mapDesc.spawnPoints.Count) continue;

                Transform targetPoint = mapDesc.spawnPoints[entry.spawnPointIndex];
                if (targetPoint == null) continue;

                GameObject selectedPrefab = SelectTankByProbability(entry.tankCandidates);

                if (selectedPrefab != null)
                {
                    SpawnManager.SpawnRequest req = new SpawnManager.SpawnRequest
                    {
                        prefab = selectedPrefab,
                        spawnPoint = targetPoint,
                        team = entry.team,
                        isCaptain = entry.isCaptain,
                        isBoss = entry.isBoss,
                        spawnPointIndex = currentId
                    };
                    requests.Add(req);
                }
            }
        }

        // 2. アイテムボックスの生成リクエスト
        if (data.itemBoxEntries != null)
        {
            foreach (var entry in data.itemBoxEntries)
            {
                int currentId = uniqueSpawnId++;

                // 既に壊されていればスキップ
                if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.IsDefeated(currentId)) continue;
                if (entry.spawnPointIndex < 0 || entry.spawnPointIndex >= mapDesc.spawnPoints.Count) continue;

                Transform targetPoint = mapDesc.spawnPoints[entry.spawnPointIndex];
                if (targetPoint == null || entry.itemBoxPrefab == null) continue;

                SpawnManager.SpawnRequest req = new SpawnManager.SpawnRequest
                {
                    prefab = entry.itemBoxPrefab,
                    spawnPoint = targetPoint,
                    team = TeamType.Red, // アイテム箱はチームを使わないためダミー値
                    isCaptain = false,
                    isBoss = false,
                    spawnPointIndex = currentId
                };
                requests.Add(req);
            }
        }

        spawnManager.ExecuteSpawn(requests);
    }

    private GameObject SelectTankByProbability(List<StageData.TankCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0) return null;
        int totalWeight = 0;
        foreach (var c in candidates) totalWeight += c.probability;
        int randomValue = Random.Range(0, totalWeight);
        int currentWeight = 0;
        foreach (var c in candidates)
        {
            currentWeight += c.probability;
            if (randomValue < currentWeight) return c.tankPrefab;
        }
        return candidates[0].tankPrefab;
    }
}