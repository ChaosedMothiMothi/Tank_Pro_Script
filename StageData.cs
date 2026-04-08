using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewStageData", menuName = "Game/Stage Data")]
public class StageData : ScriptableObject
{
    [Header("基本設定")]
    public string stageName = "Stage 1";
    public GameObject mapPrefab;

    [Header("Stage Rules Settings")]
    [Tooltip("休憩エリアなどで3,2,1のカウントダウンを飛ばす場合はチェック")]
    public bool skipStartCountdown = false;

    // ★追加: このステージでは誰もダメージを受けない（死なない）ようにする
    [Tooltip("休憩エリアなど、ダメージを無効化して死なないようにする場合はチェック")]
    public bool isInvincibleStage = false;

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