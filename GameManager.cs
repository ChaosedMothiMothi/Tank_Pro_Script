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

    // ★修正: 元々あったパーツ専用のテキスト(partsText)を削除しました。
    // 代わりに下の「simpleModeStatusText」1つだけで残機とパーツ数を両方表示させます。

    [Header("Drop Settings")]
    public GameObject defaultPartsItemPrefab;

    public bool IsFinished { get; private set; } = false;
    public bool IsGameStarted { get; private set; } = false;

    public int CurrentParts { get; private set; } = 0;

    private List<TankStatus> _allTanks = new List<TankStatus>();
    private TankStatus _playerTank;

    // フェード用の画像参照変数
    private UnityEngine.UI.Image _fadeImage;

    [Header("Simple Mode UI Settings")]
    [Tooltip("シンプルモード用の「残機・パーツ数」を表示するテキストUIをアタッチしてください")]
    public TMPro.TextMeshProUGUI simpleModeStatusText;

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

        foreach (var tank in FindObjectsByType<TankStatus>(FindObjectsSortMode.None))
        {
            RegisterTank(tank);
        }

        // フェードインとゲーム開始カウントダウン
        StartCoroutine(FadeInRoutine());
        StartCoroutine(GameStartRoutine());

        // アタッチされたUIのテキストを初回更新する
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            UpdateSimpleModeUI();
        }
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

        // プレイヤーが生成されるのを少しだけ待つ
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

        // ★修正: deadTank が _playerTank かつ team == Blue の場合のみ敗北
        if (deadTank == _playerTank && deadTank.team == TeamType.Blue)
        {
            FinishGame(false);
            return;
        }

        // 赤チームの敵が倒された場合
        if (deadTank.team == TeamType.Red)
        {
            bool hasBossOrCaptain = false;
            bool bossOrCaptainAlive = false;
            int redCount = 0;

            foreach (var t in _allTanks)
            {
                if (t != null && t.team == TeamType.Red)
                {
                    if (t.isBoss || t.isCaptain)
                    {
                        hasBossOrCaptain = true;
                        if (!t.IsDead) bossOrCaptainAlive = true;
                    }

                    if (!t.IsDead) redCount++;
                }
            }

            if (hasBossOrCaptain)
            {
                if (!bossOrCaptainAlive) FinishGame(true);
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

            if (isWin)
            {
                StartCoroutine(AutoNextStageRoutine());
                return;
            }
        }

        // シンプルモードの敗北時や、通常モードの場合はリザルト画面を表示
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
        UpdateSimpleModeUI();
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
            UpdateSimpleModeUI();
            return true;
        }
        return false;
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

    // ==========================================
    // フェードイン・アウト＆自動画面遷移
    // ==========================================
    private IEnumerator AutoNextStageRoutine()
    {
        if (startText != null)
        {
            startText.gameObject.SetActive(true);
            startText.text = "STAGE CLEAR!";
            startText.color = Color.yellow;
        }

        yield return new WaitForSeconds(2.5f);
        yield return StartCoroutine(FadeOutRoutine());

        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.HasNextStage())
        {
            GlobalGameManager.Instance.GoToNextStage();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        else
        {
            if (!string.IsNullOrEmpty(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
            }
            else
            {
                SceneManager.LoadScene(0);
            }
        }
    }

    private void SetupFadeCanvas()
    {
        if (_fadeImage != null) return;

        GameObject canvasObj = new GameObject("FadeCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform, false);
        _fadeImage = imageObj.AddComponent<UnityEngine.UI.Image>();
        _fadeImage.color = Color.black;

        RectTransform rt = _fadeImage.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
    }

    private IEnumerator FadeInRoutine()
    {
        SetupFadeCanvas();
        _fadeImage.color = new Color(0, 0, 0, 1f);
        _fadeImage.raycastTarget = true;

        float duration = 0.5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _fadeImage.color = new Color(0, 0, 0, 1.0f - (elapsed / duration));
            yield return null;
        }
        _fadeImage.raycastTarget = false;
    }

    private IEnumerator FadeOutRoutine()
    {
        SetupFadeCanvas();
        _fadeImage.gameObject.SetActive(true);
        _fadeImage.raycastTarget = true;

        float duration = 0.5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _fadeImage.color = new Color(0, 0, 0, elapsed / duration);
            yield return null;
        }
    }

    // ==========================================
    // UI更新処理
    // ==========================================
    private void UpdateSimpleModeUI()
    {
        if (GlobalGameManager.Instance == null) return;

        if (simpleModeStatusText != null)
        {
            simpleModeStatusText.text = $"LIVES: {GlobalGameManager.Instance.playerLives}\nPARTS: {CurrentParts}";
        }
    }

    // ==========================================
    // プレイヤーの残機を増やす
    // ==========================================
    public void AddPlayerLife()
    {
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            GlobalGameManager.Instance.playerLives++;
            UpdateSimpleModeUI();
        }
    }
}