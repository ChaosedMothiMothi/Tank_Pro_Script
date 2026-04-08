using UnityEngine;
using System.Collections.Generic;

public class SpawnManager : MonoBehaviour
{
    public void ExecuteSpawn(List<SpawnRequest> requests)
    {
        foreach (var req in requests)
        {
            SpawnOneTank(req);
        }
    }

    private void SpawnOneTank(SpawnRequest req)
    {
        if (req.prefab == null || req.spawnPoint == null) return;

        // 生成
        GameObject tankObj = Instantiate(req.prefab, req.spawnPoint.position, req.spawnPoint.rotation);

        // 戦車の場合の初期化
        TankStatus status = tankObj.GetComponentInChildren<TankStatus>();
        if (status != null)
        {
            status.SetTeam(req.team, req.isCaptain, req.isBoss, req.spawnPointIndex);
        }

        // アイテムボックスの場合の初期化（子オブジェクトにある場合も考慮）
        ItemBoxController box = tankObj.GetComponentInChildren<ItemBoxController>();
        if (box != null)
        {
            box.SetSpawnIndex(req.spawnPointIndex);
        }
    }

    public class SpawnRequest
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public TeamType team;
        public bool isCaptain;
        public bool isBoss;
        public int spawnPointIndex;
    }
}