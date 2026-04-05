using UnityEngine;
using System.Collections.Generic;

public class SpawnManager : MonoBehaviour
{
    // 外部から呼ばれて生成を実行するだけ
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

        // 初期化 (TankStatus等を取得して設定)
        TankStatus status = tankObj.GetComponent<TankStatus>();
        if (status != null)
        {
            status.SetTeam(req.team, req.isCaptain, req.isBoss);
        }

        // 必要ならAIコントローラーの初期化など
        // EnemyTankController ai = tankObj.GetComponent<EnemyTankController>();
        // if (ai != null) ai.Init(...);
    }

    // StageLoaderから渡されるデータ構造
    public class SpawnRequest
    {
        public GameObject prefab;
        public Transform spawnPoint;
        public TeamType team;
        public bool isCaptain;
        public bool isBoss;
        public int spawnPointIndex; // ★追加
    }
}