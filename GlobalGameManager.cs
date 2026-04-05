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
    [Tooltip("シンプルモード開始時の残機数")]
    public int defaultPlayerLives = 3;
    public int playerLives = 3;

    // ステージごとの撃破済みオブジェクトのインデックスを保存
    private Dictionary<int, HashSet<int>> _defeatedEntities = new Dictionary<int, HashSet<int>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ★メニュー画面のボタンから呼び出す関数（シーン名を引数に渡す）
    public void StartSimpleModeAndLoad(string battleSceneName)
    {
        isSimpleMode = true;
        currentSimpleStageIndex = 0;
        savedParts = 0;
        playerLives = defaultPlayerLives;
        _defeatedEntities.Clear();

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
            _defeatedEntities[currentSimpleStageIndex] = new HashSet<int>(); // 次のステージ用に空枠を作成
        }
    }

    // 死亡時にインデックスを記録
    public void RecordDefeat(int spawnIndex)
    {
        if (!isSimpleMode || spawnIndex < 0) return;
        if (!_defeatedEntities.ContainsKey(currentSimpleStageIndex))
        {
            _defeatedEntities[currentSimpleStageIndex] = new HashSet<int>();
        }
        _defeatedEntities[currentSimpleStageIndex].Add(spawnIndex);
    }

    // すでに撃破済みか確認
    public bool IsDefeated(int spawnIndex)
    {
        if (!isSimpleMode) return false;
        if (_defeatedEntities.ContainsKey(currentSimpleStageIndex))
        {
            return _defeatedEntities[currentSimpleStageIndex].Contains(spawnIndex);
        }
        return false;
    }
}