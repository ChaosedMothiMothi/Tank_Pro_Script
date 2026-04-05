using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewStageData", menuName = "Game/Stage Data")]
public class StageData : ScriptableObject
{
    [Header("基本設定")]
    public string stageName = "Stage 1";
    public GameObject mapPrefab;

    [Header("UI Settings")]
    [Tooltip("休憩エリアなどで3,2,1のカウントダウンを飛ばす場合はチェック")]
    public bool skipStartCountdown = false;

    [Header("戦車 配置リスト")]
    [Tooltip("ステージ開始時に配置される戦車のリスト")]
    public List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

    // ★追加: アイテムボックス配置リスト
    [Header("アイテムボックス 配置リスト")]
    [Tooltip("ステージ開始時に配置されるアイテムボックスのリスト")]
    public List<ItemBoxEntry> itemBoxEntries = new List<ItemBoxEntry>();

    [System.Serializable]
    public class SpawnEntry
    {
        [Header("場所")]
        [Tooltip("MapDescriptorのSpawnPointsのインデックス番号")]
        public int spawnPointIndex = 0;

        [Header("所属")]
        public TeamType team = TeamType.Red;
        [Tooltip("隊長設定、こいつが倒されるとそのチームは敗北になります")]
        public bool isCaptain = false;

        [Tooltip("ボス設定。ONの場合、専用の大型HPバーが画面上部に出現します")]
        public bool isBoss = false;

        [Header("戦車の種類（確率設定）")]
        [Tooltip("ここに出現する可能性のある戦車リスト。合計100%になるように設定推奨")]
        public List<TankCandidate> tankCandidates = new List<TankCandidate>();
    }

    [System.Serializable]
    public class TankCandidate
    {
        public GameObject tankPrefab;
        [Range(0, 100)] public int probability = 100; // 確率
    }

    // ★追加: アイテムボックス用のデータクラス
    [System.Serializable]
    public class ItemBoxEntry
    {
        [Header("場所")]
        [Tooltip("MapDescriptorのSpawnPointsのインデックス番号")]
        public int spawnPointIndex = 0;

        [Header("アイテムボックスのプレハブ")]
        public GameObject itemBoxPrefab;
    }
}