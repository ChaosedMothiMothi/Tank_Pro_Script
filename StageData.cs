using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewStageData", menuName = "Game/Stage Data")]
public class StageData : ScriptableObject
{
    [Header("基本設定")]
    public string stageName = "Stage 1";
    public GameObject mapPrefab; // MapDescriptor付きの地形プレハブ

    [Header("配置リスト")]
    [Tooltip("ステージ開始時に配置される戦車のリスト")]
    public List<SpawnEntry> spawnEntries = new List<SpawnEntry>();

    [System.Serializable]
    public class SpawnEntry
    {
        [Header("場所")]
        [Tooltip("MapDescriptorのSpawnPointsのインデックス番号")]
        public int spawnPointIndex = 0;

        [Header("所属")]
        public TeamType team = TeamType.Red;
        public bool isCaptain = false;

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
}