using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject soloMenu;
    [SerializeField] private GameObject multiMenu;
    [SerializeField] private GameObject collectionMenu;

    // 現在表示されているパネルを保持
    private GameObject currentPanel;

    void Start()
    {
        // 最初はメインメニューを表示
        ShowPanel(mainMenu);
    }

    public void ShowPanel(GameObject nextPanel)
    {
        if (currentPanel != null) currentPanel.SetActive(false);

        nextPanel.SetActive(true);
        currentPanel = nextPanel;
    }

    // ボタンから呼び出す用（例：戻るボタン）
    public void BackToMain() => ShowPanel(mainMenu);
}