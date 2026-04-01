using UnityEngine;

public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    [Header("Muzzle Flash")]
    [SerializeField] private GameObject muzzleFlashPrefab;

    [Tooltip("跳弾発生時のエフェクト")]
    [SerializeField] private GameObject reflectionPrefab;

    [Header("Hit Effects")]
    [Tooltip("通常弾が壁や敵に当たった時のエフェクト")]
    [SerializeField] private GameObject standardHitPrefab;

    [Tooltip("爆発弾や地雷が爆発した時のエフェクト")]
    [SerializeField] private GameObject explosionPrefab;

    [Header("弾の発射音")]
    [SerializeField] private AudioClip ShootSound;

    [Header("弾の爆発音")]
    [SerializeField] private AudioClip ShootExplosion;

    [Header("汎用爆発音")]
    [SerializeField] private AudioClip Explosion;

    [Header("被弾爆発音")]
    [SerializeField] private AudioClip DamageExplosion;

    [Header("跳弾音")]
    [SerializeField] private AudioClip RefrectSound;

    private AudioSource audioSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        // AudioSourceを取得（なければ追加）
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    // --- マズルフラッシュ ---
    public void PlayMuzzleFlash(Transform firePoint)
    {
        if (muzzleFlashPrefab == null) return;

        // 親（firePoint）を指定せずに生成する
        // これにより、Prefab本来のスケール (1,1,1) が維持されます
        GameObject flash = Instantiate(muzzleFlashPrefab, firePoint.position, firePoint.rotation);

        // ★重要: SetParentは行わない
        // flash.transform.SetParent(firePoint); 

        // 念のためスケールを (1,1,1) に強制リセット（Prefab設定を尊重）
        flash.transform.localScale = Vector3.one;

        // もしサイズをもっと大きくしたいなら、ここを (2,2,2) などに変えてください
        // flash.transform.localScale = Vector3.one * 2.0f; 

        Destroy(flash, 0.5f);
    }

    // --- 通常弾の着弾エフェクト ---
    public void PlayStandardHit(Vector3 position, Vector3 normal)
    {
        if (standardHitPrefab == null) return;
        // 法線（反射方向）に合わせてエフェクトの向きを調整
        GameObject effect = Instantiate(standardHitPrefab, position, Quaternion.LookRotation(normal));
        Destroy(effect, 1.0f);
    }

    // --- 弾の跳弾エフェクト ---
    public void PlayWallHit(Vector3 position, Vector3 normal)
    {
        if (standardHitPrefab == null) return;
        // 法線（反射方向）に合わせてエフェクトの向きを調整
        GameObject effect = Instantiate(reflectionPrefab, position, Quaternion.LookRotation(normal));
        Destroy(effect, 0.5f);
    }

    // --- 爆発エフェクト ---
    public void PlayExplosion(Vector3 position)
    {
        if (explosionPrefab == null) return;
        GameObject effect = Instantiate(explosionPrefab, position, Quaternion.identity);
        Destroy(effect, 2.0f);
    }

    // 音を鳴らす関数
    public void ShotSound()
    {
        audioSource.PlayOneShot(ShootSound);
    }

    public void ShootExplode()
    {
        audioSource.PlayOneShot(ShootExplosion);
    }

    public void Explode()
    {
        audioSource.PlayOneShot(Explosion);
    }

    public void DamageExplode()
    {
        audioSource.PlayOneShot(DamageExplosion);
    }

    public void RefrectionSound()
    {
        audioSource.PlayOneShot(RefrectSound);
    }

}