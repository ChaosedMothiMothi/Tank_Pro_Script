using UnityEngine;

public class GlobalGameManager : MonoBehaviour
{
    // これだけが static (クラス名でアクセスできる唯一の入り口)
    public static GlobalGameManager Instance { get; private set; }

    // これはインスタンス変数 (Instanceを通してアクセスする)
    public StageData SelectedStage;

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
}