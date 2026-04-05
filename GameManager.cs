using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Scene Settings")]
    [SerializeField] private string menuSceneName = "MenuScene";

    [Header("UI Manager")]
    [SerializeField] private ResultUIManager resultUIManager;

    [Header("Simple Mode Settings")]
    [Tooltip("敵が落とすパーツのプレハブ")]
    [SerializeField] private GameObject partsItemPrefab;

    // ★追加: 画面中央に出す「3, 2, 1, START!」用のテキスト
    [SerializeField] private TMPro.TextMeshProUGUI startText;

    public int CurrentParts { get; private set; } = 0;

    private const TeamType PlayerTeam = TeamType.Blue;

    // --- 状態管理フラグ ---
    public bool IsFinished { get; private set; } = false;

    // ★復活: ゲームが開始されたかどうかのフラグ（これがないと戦車が動けない）
    public bool IsGameStarted { get; private set; } = false;

    // --- 戦車リスト ---
    private List<TankStatus> _allTanks = new List<TankStatus>();
    private bool _hasMultipleTeamsSpawned = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        // ★修正: Awakeの段階でパーツを引き継いでおく（他より早く設定する）
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            CurrentParts = GlobalGameManager.Instance.savedParts;
        }
        else
        {
            CurrentParts = 0;
        }
    }

    private void Start()
    {
        IsFinished = false;
        IsGameStarted = false;
        _allTanks.Clear();

        // ★追加: シンプルモードなら前回のパーツを引き継ぐ
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            CurrentParts = GlobalGameManager.Instance.savedParts;
        }
        else
        {
            CurrentParts = 0; // 通常モードなら0から
        }

        foreach (var tank in FindObjectsByType<TankStatus>(FindObjectsSortMode.None))
        {
            RegisterTank(tank);
        }

        StartCoroutine(GameStartRoutine());
    }

    // ゲーム開始前のちょっとした待機時間（Ready... GO! の代わり）
    private IEnumerator GameStartRoutine()
    {
        // ★追加: ステージデータを見てスキップするか判定
        bool skip = false;
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.SelectedStage != null)
        {
            skip = GlobalGameManager.Instance.SelectedStage.skipStartCountdown;
        }

        if (skip)
        {
            // スキップする場合は即座にゲーム開始
            IsGameStarted = true;
            if (startText != null) startText.gameObject.SetActive(false);
            yield break;
        }

        if (startText != null)
        {
            startText.gameObject.SetActive(true);

            startText.text = "3";
            yield return new WaitForSeconds(0.7f);

            startText.text = "2";
            yield return new WaitForSeconds(0.7f);

            startText.text = "1";
            yield return new WaitForSeconds(0.7f);

            startText.text = "START!";
            IsGameStarted = true; // ★ここで操作可能になる
            Debug.Log("<color=green>[Game Started]</color> 戦闘開始！");

            yield return new WaitForSeconds(1.0f);
            startText.gameObject.SetActive(false); // 文字を消す
        }
        else
        {
            // テキストがセットされていない場合は今まで通り1.5秒待つだけ
            yield return new WaitForSeconds(1.5f);
            IsGameStarted = true;
        }
    }

    public void RegisterTank(TankStatus tank)
    {
        if (IsFinished) return;
        if (!_allTanks.Contains(tank))
        {
            _allTanks.Add(tank);
            CheckTeamCount(); // ★エラーになっていた関数を呼び出し
        }
    }

    public void OnTankDead(TankStatus tank)
    {
        if (IsFinished) return;
        CheckWinCondition();
    }

    // ★復活: チーム数を数える関数（エラーの原因だった部分��
    private void CheckTeamCount()
    {
        if (_hasMultipleTeamsSpawned) return;

        int teamCount = _allTanks
            .Where(t => !t.IsDead)
            .Select(t => t.team)
            .Distinct()
            .Count();

        if (teamCount >= 2)
        {
            _hasMultipleTeamsSpawned = true;
        }
    }

    private void CheckWinCondition()
    {
        if (IsFinished) return;
        if (!_hasMultipleTeamsSpawned) return;

        var aliveTanks = _allTanks.Where(t => !t.IsDead).ToList();

        // 1. プレイヤーチーム（PlayerTeam）の敗北判定
        if (CheckTeamDefeat(PlayerTeam, aliveTanks))
        {
            FinishGame(false);
            return;
        }

        // 2. 敵チームの全滅判定
        var enemyTeams = aliveTanks
            .Select(t => t.team)
            .Where(t => t != PlayerTeam)
            .Distinct()
            .ToList();

        if (enemyTeams.Count == 0)
        {
            FinishGame(true);
            return;
        }

        // 3. 残っている敵チームが敗北条件を満たしているか
        bool allEnemiesLost = true;
        foreach (var eTeam in enemyTeams)
        {
            if (!CheckTeamDefeat(eTeam, aliveTanks))
            {
                allEnemiesLost = false;
                break;
            }
        }

        if (allEnemiesLost)
        {
            FinishGame(true);
        }
    }

    // ★追加: クリアエリア（安全地帯）などに触れた時に強制的にクリア扱いにする
    public void ForceWin()
    {
        if (IsFinished) return;
        FinishGame(true);
    }

    private bool CheckTeamDefeat(TeamType targetTeam, List<TankStatus> currentAliveTanks)
    {
        var teamSurvivors = currentAliveTanks.Where(t => t.team == targetTeam).ToList();
        if (teamSurvivors.Count == 0) return true;

        bool hasCaptainOriginally = _allTanks.Any(t => t.team == targetTeam && t.isCaptain);
        if (hasCaptainOriginally)
        {
            bool captainAlive = teamSurvivors.Any(t => t.isCaptain);
            if (!captainAlive) return true;
        }

        return false;
    }

    private void FinishGame(bool isWin)
    {
        IsFinished = true;

        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            if (isWin)
            {
                GlobalGameManager.Instance.savedParts = CurrentParts;
            }
            else
            {
                // ★負けたら残機を1減らす
                GlobalGameManager.Instance.playerLives--;
            }
        }

        if (resultUIManager != null) resultUIManager.ShowResult(isWin);


    }

    public bool IsGameFinished()
    {
        return IsFinished;
    }

    public void AddParts(int amount)
    {
        CurrentParts += amount;
        Debug.Log($"<color=yellow>[Parts Collected]</color> 現在のパーツ: {CurrentParts}");

        // ★追加: 取得した瞬間にGlobalに保存する（リトライしても失わず、稼ぎ防止にもなる）
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            GlobalGameManager.Instance.savedParts = CurrentParts;
        }
    }

    public GameObject GetPartsItemPrefab() => partsItemPrefab;

    public void RetryGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMenu()
    {
        SceneManager.LoadScene(menuSceneName);
    }
}