using UnityEngine;
using System.Collections.Generic;

public class GlobalGameManager : MonoBehaviour
{
    public static GlobalGameManager Instance { get; private set; }

    [Header("現在選択されているステージ（通常モード用）")]
    public StageData SelectedStage;

    [Header("--- シンプルモード設定 ---")]
    public List<StageData> simpleModeStages = new List<StageData>();
    public bool isSimpleMode = false;
    public int currentSimpleStageIndex = 0;
    public int savedParts = 0;

    [Header("--- 残機システム ---")]
    public int defaultPlayerLives = 3;
    public int playerLives = 3;

    private Dictionary<int, HashSet<int>> _defeatedEntities = new Dictionary<int, HashSet<int>>();

    // ==========================================
    // ★追加・修正：ステータスや装備の引き継ぎデータ
    // ==========================================
    public int savedLevelBounces, savedLevelMaxAmmo, savedLevelMoveSpeed, savedLevelShellSpeed;
    public int savedLevelMineLimit, savedLevelRotationSpeed, savedLevelShellDamage, savedLevelMineDamage;
    public GameObject savedShellPrefab;
    public GameObject savedMinePrefab;
    public ShieldData savedShieldData;
    public bool savedIsBerserker = false; // ★バーサーカーモードの保存用

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else Destroy(gameObject);
    }

    public void StartSimpleModeAndLoad(string battleSceneName)
    {
        isSimpleMode = true;
        currentSimpleStageIndex = 0;
        savedParts = 0;
        playerLives = defaultPlayerLives;
        _defeatedEntities.Clear();

        // 初期化
        savedLevelBounces = savedLevelMaxAmmo = savedLevelMoveSpeed = savedLevelShellSpeed = 0;
        savedLevelMineLimit = savedLevelRotationSpeed = savedLevelShellDamage = savedLevelMineDamage = 0;
        savedShellPrefab = savedMinePrefab = null;
        savedShieldData = null;
        savedIsBerserker = false;

        if (simpleModeStages.Count > 0)
        {
            SelectedStage = simpleModeStages[0];
            UnityEngine.SceneManagement.SceneManager.LoadScene(battleSceneName);
        }
    }

    public bool HasNextStage() => isSimpleMode && currentSimpleStageIndex < simpleModeStages.Count - 1;

    public void GoToNextStage()
    {
        if (HasNextStage())
        {
            currentSimpleStageIndex++;
            SelectedStage = simpleModeStages[currentSimpleStageIndex];
            _defeatedEntities[currentSimpleStageIndex] = new HashSet<int>();
        }
    }

    public void RecordDefeat(int spawnIndex)
    {
        if (!isSimpleMode || spawnIndex < 0) return;
        if (!_defeatedEntities.ContainsKey(currentSimpleStageIndex)) _defeatedEntities[currentSimpleStageIndex] = new HashSet<int>();
        _defeatedEntities[currentSimpleStageIndex].Add(spawnIndex);
    }

    public bool IsDefeated(int spawnIndex)
    {
        if (!isSimpleMode) return false;
        if (_defeatedEntities.ContainsKey(currentSimpleStageIndex)) return _defeatedEntities[currentSimpleStageIndex].Contains(spawnIndex);
        return false;
    }

    // ★ステータスの保存処理
    public void SavePlayerStats(TankStatus player)
    {
        savedLevelBounces = player.levelBounces;
        savedLevelMaxAmmo = player.levelMaxAmmo;
        savedLevelMoveSpeed = player.levelMoveSpeed;
        savedLevelShellSpeed = player.levelShellSpeed;
        savedLevelMineLimit = player.levelMineLimit;
        savedLevelRotationSpeed = player.levelRotationSpeed;
        savedLevelShellDamage = player.levelShellDamage;
        savedLevelMineDamage = player.levelMineDamage;
        savedShellPrefab = player.GetShellPrefab();
        savedMinePrefab = player.GetMinePrefab();
        savedShieldData = player.currentShieldData;
        savedIsBerserker = player.isBerserkerMode; // ★バーサーカー状態の保存
    }
}