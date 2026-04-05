using UnityEngine;

public class WeakPoint : MonoBehaviour
{
    [Tooltip("ボスの本体Status")]
    public TankStatus bossStatus;

    private Renderer _renderer;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (bossStatus == null)
        {
            bossStatus = GetComponentInParent<TankStatus>();
        }
    }

    // ★修正: 第2引数 attacker を受け取れるように変更
    public void TakeWeakPointDamage(int baseDamage, TankStatus attacker = null)
    {
        if (MaterialFlasher.Instance != null && _renderer != null)
        {
            MaterialFlasher.Instance.Flash(_renderer);
        }

        if (bossStatus != null)
        {
            // ボス側にダメージとアタッカー情報を流す
            bossStatus.TakeWeakPointDamage(baseDamage, attacker);
        }
    }
}