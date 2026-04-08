using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    public string nextSceneName;

    [Header("Player Indicator Settings")]
    [Tooltip("ゲーム開始時に表示する文字")]
    public string playerIndicatorText = "▼ YOU";
    [Tooltip("インジケーターの色")]
    public Color playerIndicatorColor = Color.yellow;
    [Tooltip("文字の大きさ（標準は 5〜10 程度）")]
    public float playerIndicatorSize = 8f;
    [Tooltip("プレイヤーの頭上どれくらいの高さに表示するか")]
    public float playerIndicatorOffsetY = 4.0f;

    [Header("UI References")]
    [SerializeField] private ResultUIManager resultUIManager;
    [SerializeField] private TMPro.TextMeshProUGUI startText;
    [SerializeField] private TMPro.TextMeshProUGUI partsText;

    [Header("Drop Settings")]
    public GameObject defaultPartsItemPrefab;

    public bool IsFinished { get; private set; } = false;
    public bool IsGameStarted { get; private set; } = false;

    public int CurrentParts { get; private set; } = 0;

    private List<TankStatus> _allTanks = new List<TankStatus>();
    private TankStatus _playerTank;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
            CurrentParts = GlobalGameManager.Instance.savedParts;
        else
            CurrentParts = 0;
    }

    private void Start()
    {
        IsFinished = false;
        IsGameStarted = false;
        _allTanks.Clear();

        UpdatePartsText();

        foreach (var tank in FindObjectsByType<TankStatus>(FindObjectsSortMode.None))
        {
            RegisterTank(tank);
        }

        StartCoroutine(GameStartRoutine());
    }

    private IEnumerator GameStartRoutine()
    {
        bool skip = false;
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.SelectedStage != null)
        {
            skip = GlobalGameManager.Instance.SelectedStage.skipStartCountdown;
        }

        if (skip)
        {
            IsGameStarted = true;
            if (startText != null) startText.gameObject.SetActive(false);
            yield break;
        }

        // ★追加・修正: プレイヤーが生成されるのを少しだけ待つ（動的スポーン対策）
        float waitTimer = 0f;
        while (_playerTank == null && waitTimer < 1.0f)
        {
            foreach (var tank in FindObjectsByType<TankStatus>(FindObjectsSortMode.None))
            {
                if (tank.team == TeamType.Blue)
                {
                    _playerTank = tank;
                    RegisterTank(tank);
                    break;
                }
            }
            waitTimer += Time.deltaTime;
            yield return null;
        }

        // ★修正: TextMeshProを使って、確実に綺麗に表示する
        GameObject playerIndicator = null;
        if (_playerTank != null)
        {
            playerIndicator = new GameObject("PlayerIndicator");
            playerIndicator.transform.SetParent(_playerTank.transform);
            playerIndicator.transform.localPosition = new Vector3(0, playerIndicatorOffsetY, 0);

            TMPro.TextMeshPro tmpro = playerIndicator.AddComponent<TMPro.TextMeshPro>();
            tmpro.text = playerIndicatorText;
            tmpro.color = playerIndicatorColor;
            tmpro.fontSize = playerIndicatorSize;
            tmpro.alignment = TMPro.TextAlignmentOptions.Center;
            tmpro.fontStyle = TMPro.FontStyles.Bold;

            // アニメーションを開始
            StartCoroutine(AnimateIndicatorRoutine(playerIndicator));
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

            // ゲーム開始と同時にインジケーターを消す
            if (playerIndicator != null) Destroy(playerIndicator);

            IsGameStarted = true;
            yield return new WaitForSeconds(1.0f);
            startText.gameObject.SetActive(false);
        }
        else
        {
            yield return new WaitForSeconds(1.5f);
            if (playerIndicator != null) Destroy(playerIndicator);
            IsGameStarted = true;
        }
    }

    private IEnumerator AnimateIndicatorRoutine(GameObject indicator)
    {
        float t = 0;
        Vector3 basePos = new Vector3(0, playerIndicatorOffsetY, 0);
        while (indicator != null)
        {
            t += Time.deltaTime * 5f;
            indicator.transform.localPosition = basePos + new Vector3(0, Mathf.Sin(t) * 0.3f, 0);

            if (Camera.main != null)
            {
                indicator.transform.rotation = Camera.main.transform.rotation;
            }
            yield return null;
        }
    }

    public void RegisterTank(TankStatus tank)
    {
        if (!_allTanks.Contains(tank))
        {
            _allTanks.Add(tank);
            if (tank.team == TeamType.Blue) _playerTank = tank;
        }
    }

    public void OnTankDead(TankStatus deadTank)
    {
        if (IsFinished) return;

        if (deadTank.team == TeamType.Blue)
        {
            FinishGame(false);
        }
        else if (deadTank.team == TeamType.Red)
        {
            bool hasBoss = false;
            bool bossAlive = false;
            int redCount = 0;

            foreach (var t in _allTanks)
            {
                if (t != null && t.team == TeamType.Red)
                {
                    if (t.isBoss) { hasBoss = true; if (!t.IsDead) bossAlive = true; }
                    if (!t.IsDead) redCount++;
                }
            }

            if (hasBoss)
            {
                if (!bossAlive) FinishGame(true);
            }
            else
            {
                if (redCount == 0) FinishGame(true);
            }
        }
    }

    public void ForceWin()
    {
        if (!IsFinished) FinishGame(true);
    }

    private void FinishGame(bool isWin)
    {
        IsFinished = true;

        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            if (_playerTank != null)
            {
                GlobalGameManager.Instance.SavePlayerStats(_playerTank);
            }
            GlobalGameManager.Instance.savedParts = CurrentParts;

            if (!isWin)
            {
                GlobalGameManager.Instance.playerLives--;
            }
        }

        if (resultUIManager != null) resultUIManager.ShowResult(isWin);
    }

    public bool IsGameFinished() => IsFinished;
    public GameObject GetPartsItemPrefab() => defaultPartsItemPrefab;

    public void AddParts(int amount)
    {
        CurrentParts += amount;
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            GlobalGameManager.Instance.savedParts = CurrentParts;
        }
        UpdatePartsText();
    }

    public bool ConsumeParts(int amount)
    {
        if (CurrentParts >= amount)
        {
            CurrentParts -= amount;
            if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
            {
                GlobalGameManager.Instance.savedParts = CurrentParts;
            }
            UpdatePartsText();
            return true;
        }
        return false;
    }

    private void UpdatePartsText()
    {
        if (partsText != null) partsText.text = $"Parts: {CurrentParts}";
    }

    public void RetryGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void ReturnToMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(0);
    }
}