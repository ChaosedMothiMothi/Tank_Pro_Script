using UnityEngine;

[Tooltip("爆発時に爆風ではなく、8方位に弾を発射する特殊な地雷")]
public class SMineController : MineController
{
    [Header("S-Mine Exclusive Settings")]
    [Tooltip("爆発時に8方位に発射される弾のプレハブ")]
    public GameObject shellPrefab;

    [Tooltip("弾を発射する際、地雷の中心からどれくらい外側に離すか")]
    public float spawnOffsetRadius = 0.8f;

    [Tooltip("弾を発射する高さ")]
    public float spawnOffsetY = 0.5f;

    // 親クラスの「爆風ダメージ処理」を打ち消し、弾の発射処理に上書きする
    protected override void ApplyExplosionDamage()
    {
        if (mineData == null || shellPrefab == null) return;

        int totalDamage = mineData.damage;
        if (_ownerStatus != null)
        {
            totalDamage = _ownerStatus.GetTotalMineDamage(mineData.damage);
        }

        // 8分割したダメージ（最低でも1ダメージは保証する）
        int damagePerShell = Mathf.Max(1, totalDamage / 8);
        GameObject ownerObj = _ownerStatus != null ? _ownerStatus.gameObject : this.gameObject;

        float angleStep = 360f / 8f;

        // 弾同士がぶつかって相殺しないように、生成した弾のコライダーを記録しておく配列
        Collider[] spawnedColliders = new Collider[8];

        for (int i = 0; i < 8; i++)
        {
            float angle = i * angleStep;
            Quaternion rotation = Quaternion.Euler(0, angle, 0);
            Vector3 direction = rotation * Vector3.forward;

            // ★修正: 中心ではなく、向いている方向に少しズラした位置から発射する
            Vector3 spawnPos = transform.position + Vector3.up * spawnOffsetY + direction * spawnOffsetRadius;

            GameObject shellObj = Instantiate(shellPrefab, spawnPos, rotation);
            spawnedColliders[i] = shellObj.GetComponent<Collider>();

            ShellController shellCtrl = shellObj.GetComponent<ShellController>();

            if (shellCtrl != null)
            {
                if (shellCtrl.shellData != null)
                {
                    shellCtrl.shellData = Instantiate(shellCtrl.shellData);
                    shellCtrl.shellData.damage = damagePerShell;
                }
                shellCtrl.Launch(ownerObj, 0);
            }
        }

        // ★追加: 同時に発射された8発の弾同士は、お互いにぶつかっても無視する（相殺を防ぐ）
        for (int i = 0; i < 8; i++)
        {
            if (spawnedColliders[i] == null) continue;
            for (int j = i + 1; j < 8; j++)
            {
                if (spawnedColliders[j] == null) continue;
                Physics.IgnoreCollision(spawnedColliders[i], spawnedColliders[j]);
            }
        }
    }
}