using UnityEngine;
using UnityEngine.SceneManagement;

public class ClearArea : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        TankStatus status = other.GetComponentInParent<TankStatus>();
        if (status == null) status = other.GetComponent<TankStatus>();

        if (status != null && status.team == TeamType.Blue && !status.IsDead)
        {
            // ★追加: クリア時にプレイヤーのステータスを保存する
            if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
            {
                GlobalGameManager.Instance.SavePlayerStats(status);

                if (GlobalGameManager.Instance.HasNextStage())
                {
                    GlobalGameManager.Instance.GoToNextStage();
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                }
                else
                {
                    if (GameManager.Instance != null) GameManager.Instance.ForceWin();
                }
            }
            else
            {
                if (GameManager.Instance != null) GameManager.Instance.ForceWin();
            }
        }
    }
}