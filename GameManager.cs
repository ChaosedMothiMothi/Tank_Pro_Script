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

    // ★重要: あなたのプロジェクトの TeamType に合わせて変更してください
    // 例: Playerチームが Blue なら TeamType.Blue
    private const TeamType PlayerTeam = TeamType.Blue;

    // 状態管理
    public bool IsFinished { get; private set; } = false;

    // 戦車リスト
    private List<TankStatus> _allTanks = new List<TankStatus>();
    private bool _hasMultipleTeamsSpawned = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        IsFinished = false;
        _allTanks.Clear();
        foreach (var tank in FindObjectsByType<TankStatus>(FindObjectsSortMode.None))
        {
            RegisterTank(tank);
        }
    }

    public void RegisterTank(TankStatus tank)
    {
        if (IsFinished) return;
        if (!_allTanks.Contains(tank))
        {
            _allTanks.Add(tank);
            CheckTeamCount();
        }
    }

    public void OnTankDead(TankStatus tank)
    {
        if (IsFinished) return;
        CheckWinCondition();
    }

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

    // ★勝敗判定ロジック
    private void CheckWinCondition()
    {
        if (IsFinished) return;
        if (!_hasMultipleTeamsSpawned) return;

        var aliveTanks = _allTanks.Where(t => !t.IsDead).ToList();

        // 1. プレイヤーチーム（PlayerTeam）の敗北判定
        if (CheckTeamDefeat(PlayerTeam, aliveTanks))
        {
            FinishGame(false); // 負け
            return;
        }

        // 2. 敵チームの全滅判定
        // 自分以外のチームを抽出
        var enemyTeams = aliveTanks
            .Select(t => t.team)
            .Where(t => t != PlayerTeam)
            .Distinct()
            .ToList(); // ToList()で実体化

        // 敵チームが1つも残っていない（Countプロパティを使う）
        if (enemyTeams.Count == 0)
        {
            FinishGame(true); // 勝ち
            return;
        }

        // 3. 残っている敵チームが「敗北条件（隊長全滅など）」を満たしているか
        bool allEnemiesLost = true;
        foreach (var eTeam in enemyTeams)
        {
            // もし「負けていない（生き残っている）」敵チームが一つでもあれば、まだ勝利ではない
            if (!CheckTeamDefeat(eTeam, aliveTanks))
            {
                allEnemiesLost = false;
                break;
            }
        }

        if (allEnemiesLost)
        {
            FinishGame(true); // 勝ち
        }
    }

    // 特定のチームが負けているか判定
    private bool CheckTeamDefeat(TeamType targetTeam, List<TankStatus> currentAliveTanks)
    {
        // そのチームの現在の生存者
        var teamSurvivors = currentAliveTanks.Where(t => t.team == targetTeam).ToList();

        // A. 全滅しているなら負け
        if (teamSurvivors.Count == 0) return true;

        // B. 「隊長」設定が存在したチームか？
        bool hasCaptainOriginally = _allTanks.Any(t => t.team == targetTeam && t.isCaptain);

        if (hasCaptainOriginally)
        {
            // 生存者の中に隊長がいるか？
            bool captainAlive = teamSurvivors.Any(t => t.isCaptain);
            if (!captainAlive)
            {
                // 隊長が全滅したので負け
                return true;
            }
        }

        return false; // まだ負けていない
    }

    private void FinishGame(bool isWin)
    {
        IsFinished = true;
        Debug.Log(isWin ? "YOU WIN!" : "YOU LOSE...");

        if (resultUIManager != null)
        {
            resultUIManager.ShowResult(isWin);
        }
    }

    public bool IsGameFinished()
    {
        return IsFinished;
    }

    public void RetryGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMenu()
    {
        SceneManager.LoadScene(menuSceneName);
    }
}