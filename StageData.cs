using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewStageData", menuName = "Game/Stage Data")]
public class StageData : ScriptableObject
{
    // ★追加: クリア条件の種類
    public enum ClearConditionType
    {
        Annihilation,       // 敵全滅 (隊長設定があれば隊長を倒せばクリア)
        Survival,           // 一定時間生存する
        ReachDestination    // 指定箇所(ゴール)に到達する
    }

    [Header("基本設定")]
    public string stageName = "Stage 1";
    public GameObject mapPrefab;

    [Header("Stage Rules Settings")]
    [Tooltip("休憩エリアなどで3,2,1のカウントダウンを飛ばす場合はチェック")]
    public bool skipStartCountdown = false;

    [Tooltip("休憩エリアなど、ダメージを無効化して死なないようにする場合はチェック")]
    public bool isInvincibleStage = false;

    // ★追加: クリア条件の設定項目
    [Header("クリア条件設定")]
    [Tooltip("このステージのクリア条件を選択してください")]
    public ClearConditionType clearCondition = ClearConditionType.Annihilation;

    [Tooltip("クリア条件が「Survival」の時、生き残る必要がある時間（秒）")]
    public float survivalTime = 60f;

    [Header("戦車 配置リスト")]
    public List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

    [Header("アイテムボックス 配置リスト")]
    public List<ItemBoxEntry> itemBoxEntries = new List<ItemBoxEntry>();

    [System.Serializable]
    public class SpawnEntry
    {
        [Header("場所")]
        public int spawnPointIndex = 0;

        [Header("設定")]
        public TeamType team = TeamType.Red;
        public bool isCaptain = false;
        public bool isBoss = false;

        [Header("候補")]
        public List<TankCandidate> tankCandidates = new List<TankCandidate>();
    }

    [System.Serializable]
    public class TankCandidate
    {
        public GameObject tankPrefab;
        [Range(0, 100)] public int probability = 100;
    }

    [System.Serializable]
    public class ItemBoxEntry
    {
        [Header("場所")]
        public int spawnPointIndex = 0;

        [Header("プレハブ")]
        public GameObject itemBoxPrefab;
    }
}