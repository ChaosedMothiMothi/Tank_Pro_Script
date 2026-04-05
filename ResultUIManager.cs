using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ResultUIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private GameObject winTextObj;
    [SerializeField] private GameObject loseTextObj;
    [SerializeField] private GameObject gameOverTextObj;

    [Header("Buttons")]
    [SerializeField] private Button retryButton;
    [SerializeField] private Button menuButton;

    // ★追加: 「次のステージへ」ボタン
    [SerializeField] private Button nextStageButton;

    private void Start()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
        if (winTextObj != null) winTextObj.SetActive(false);
        if (loseTextObj != null) loseTextObj.SetActive(false);

        if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
        if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);

        // ★追加: Nextボタンの登録
        if (nextStageButton != null) nextStageButton.onClick.AddListener(OnNextStageClicked);
    }

    public void ShowResult(bool isWin)
    {
        if (resultPanel != null) resultPanel.SetActive(true);

        if (isWin)
        {
            if (winTextObj != null) winTextObj.SetActive(true);
            if (loseTextObj != null) loseTextObj.SetActive(false);

            if (retryButton != null) retryButton.gameObject.SetActive(false);

            // ★追加: シンプルモードで「次のステージ」がある場合のみNextボタンを表示
            if (GlobalGameManager.Instance != null &&
                GlobalGameManager.Instance.isSimpleMode &&
                GlobalGameManager.Instance.HasNextStage())
            {
                if (nextStageButton != null) nextStageButton.gameObject.SetActive(true);
            }
            else
            {
                // シンプルモードの全クリ、または通常モードの場合は出さない
                if (nextStageButton != null) nextStageButton.gameObject.SetActive(false);
            }
        }
        else
        {
            if (winTextObj != null) winTextObj.SetActive(false);

            // ★残機チェック
            if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode && GlobalGameManager.Instance.playerLives <= 0)
            {
                // ゲームオーバー（コンティニュー不可）
                if (loseTextObj != null) loseTextObj.SetActive(false);
                if (gameOverTextObj != null) gameOverTextObj.SetActive(true); // ★GameOver専用テキストを表示
                if (retryButton != null) retryButton.gameObject.SetActive(false); // ★Retryを消す
            }
            else
            {
                // まだ残機あり
                if (loseTextObj != null) loseTextObj.SetActive(true);
                if (gameOverTextObj != null) gameOverTextObj.SetActive(false);
                if (retryButton != null) retryButton.gameObject.SetActive(true);
            }
            if (nextStageButton != null) nextStageButton.gameObject.SetActive(false);
        }

        if (menuButton != null) menuButton.gameObject.SetActive(true);
    }

    private void OnRetryClicked()
    {
        if (GameManager.Instance != null) GameManager.Instance.RetryGame();
    }

    private void OnMenuClicked()
    {
        if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
    }

    // ★追加: 次のステージへ進む処理
    private void OnNextStageClicked()
    {
        if (GlobalGameManager.Instance != null)
        {
            GlobalGameManager.Instance.GoToNextStage();
            // 現在のバトルシーンを再読み込み（StageLoaderが新しいSelectedStageを読み込んでくれる）
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }
}