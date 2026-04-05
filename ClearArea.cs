using UnityEngine;
using UnityEngine.SceneManagement;

public class ClearArea : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        // 触れたのが戦車かどうか
        TankStatus status = other.GetComponentInParent<TankStatus>();
        if (status == null) status = other.GetComponent<TankStatus>();

        // プレイヤー（TeamType.Blue）ならクリア処理
        if (status != null && status.team == TeamType.Blue && !status.IsDead)
        {
            if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
            {
                // ★シンプルモードの場合は、リザルトを出さずに直接次のステージへ
                if (GlobalGameManager.Instance.HasNextStage())
                {
                    GlobalGameManager.Instance.GoToNextStage();
                    SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                }
                else
                {
                    // 最後のステージだった場合は普通にクリア（Win表示）
                    if (GameManager.Instance != null) GameManager.Instance.ForceWin();
                }
            }
            else
            {
                // ★通常モードの場合は普通にクリア（Win表示）
                if (GameManager.Instance != null) GameManager.Instance.ForceWin();
            }
        }
    }
}