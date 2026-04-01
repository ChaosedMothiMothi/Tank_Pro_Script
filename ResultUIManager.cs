using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshProを使う場合

public class ResultUIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject resultPanel; // 全体を覆うパネル
    [SerializeField] private GameObject winTextObj;
    [SerializeField] private GameObject loseTextObj;

    [Header("Buttons")]
    [SerializeField] private Button retryButton;
    [SerializeField] private Button menuButton;

    private void Start()
    {
        // 最初は非表示
        if (resultPanel != null) resultPanel.SetActive(false);
        if (winTextObj != null) winTextObj.SetActive(false);
        if (loseTextObj != null) loseTextObj.SetActive(false);

        // ボタンイベント登録
        if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
        if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);
    }

    public void ShowResult(bool isWin)
    {
        if (resultPanel != null) resultPanel.SetActive(true);

        if (isWin)
        {
            if (winTextObj != null) winTextObj.SetActive(true);
            if (loseTextObj != null) loseTextObj.SetActive(false);

            // 勝ちの場合はリトライボタンを隠す（要望：メニューのみ）
            // もしリトライも出したければここを true に
            if (retryButton != null) retryButton.gameObject.SetActive(false);
        }
        else
        {
            if (winTextObj != null) winTextObj.SetActive(false);
            if (loseTextObj != null) loseTextObj.SetActive(true);

            // 負けの場合はリトライとメニューを表示
            if (retryButton != null) retryButton.gameObject.SetActive(true);
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
}