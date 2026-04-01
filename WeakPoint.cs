using UnityEngine;

public class WeakPoint : MonoBehaviour
{

    [Tooltip("ボスの本体Status")]
    public TankStatus bossStatus;

    private Renderer _renderer; // 追加

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        // もしインスペクターで未設定なら、親から探す
        if (bossStatus == null)
        {
            bossStatus = GetComponentInParent<TankStatus>();
        }
    }

    public void TakeWeakPointDamage(int baseDamage)
    {
        // --- 追加: 赤く点滅 ---
        if (MaterialFlasher.Instance != null && _renderer != null)
        {
            MaterialFlasher.Instance.Flash(_renderer);
        }

        if (bossStatus != null)
        {
            // ボス側の設定値を使ってダメージ計算させる
            bossStatus.TakeWeakPointDamage(baseDamage);
        }
    }
}