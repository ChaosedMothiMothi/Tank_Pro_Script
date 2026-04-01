using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class MainMenuController : MonoBehaviour
{
    [Header("シーン設定")]
    [Tooltip("遷移先のシーン名を正確に入力してください")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("ステージデータ")]
    [SerializeField] private List<StageData> availableStages;

    [Header("UI参照")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject buttonPrefab;

    private void Start()
    {
        // 設定チェック
        if (buttonPrefab == null || buttonContainer == null)
        {
            Debug.LogError("【エラー】Inspectorで Button Prefab または Container が設定されていません！");
            return;
        }

        if (availableStages == null || availableStages.Count == 0)
        {
            Debug.LogWarning("【警告】Available Stages が空です。ボタンが生成されません。");
            return;
        }

        // 既存のボタンをお掃除
        foreach (Transform child in buttonContainer)
        {
            Destroy(child.gameObject);
        }

        Debug.Log($"ステージ数: {availableStages.Count} 個のボタンを生成開始...");

        foreach (var stageData in availableStages)
        {
            // 1. ボタン生成
            GameObject btnObj = Instantiate(buttonPrefab, buttonContainer);

            // 2. ラベル設定
            var tmPro = btnObj.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmPro != null) tmPro.text = stageData.stageName;

            var legacyText = btnObj.GetComponentInChildren<Text>();
            if (legacyText != null) legacyText.text = stageData.stageName;

            // 3. ボタンコ��ポーネント取得
            Button btn = btnObj.GetComponent<Button>();
            if (btn == null)
            {
                Debug.LogError($"【エラー】プレハブ {buttonPrefab.name} に Button コンポーネントがありません！");
                continue;
            }

            // 4. クリックイベント登録（ローカル変数に退避）
            // ループ変数を直接使うと、全てのボタンが最後のステージになってしまうため
            StageData targetStage = stageData;

            btn.onClick.RemoveAllListeners(); // 念のためクリア

            // 明示的にリスナーを追加
            btn.onClick.AddListener(delegate { OnStageButtonClicked(targetStage); });

            Debug.Log($"ボタン生成完了: {targetStage.stageName}");
        }
    }

    // クリックされた時に呼ばれる関数
    public void OnStageButtonClicked(StageData stage)
    {
        Debug.Log($"【クリック成功】ステージ: {stage.stageName} を選択しました！");

        // GlobalGameManagerの確認・生成
        if (GlobalGameManager.Instance == null)
        {
            Debug.Log("GlobalGameManagerが見つからないため生成します...");
            GameObject go = new GameObject("GlobalGameManager");
            go.AddComponent<GlobalGameManager>();
        }

        // データをセット (GlobalGameManagerの定義に合わせて書き換えてください)
        // パターンA: staticプロパティの場合
        // GlobalGameManager.SelectedStage = stage;

        // パターンB: インスタンスプロパティの場合 (エラーが出るならこちらをコメントアウトし、上を使う)
        GlobalGameManager.Instance.SelectedStage = stage;

        Debug.Log($"シーン {gameSceneName} へ遷移します...");
        SceneManager.LoadScene(gameSceneName);
    }
}