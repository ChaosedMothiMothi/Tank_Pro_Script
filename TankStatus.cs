using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class TankStatus : MonoBehaviour
{
    [Header("Data References")]
    [SerializeField] private TankStatusData tankData;

    [Header("Prefabs & Settings (Dataにないもの)")]
    [SerializeField] private GameObject shellPrefab;
    [SerializeField] private GameObject minePrefab;
    [SerializeField] private MineData mineData;

    [Header("Death Settings")]
    [Tooltip("死亡時にバラバラにするパーツ（砲塔、車体、タイヤなど）")]
    [SerializeField] private List<GameObject> partsToScatter;
    [Tooltip("各パーツが消滅する際の爆発エフェクト")]
    [SerializeField] private GameObject partExplosionEffect;

    [Header("Game Rule Settings")]
    public TeamType team;
    public bool isCaptain = false;

    [Header("Shield Configuration")]
    [SerializeField] private Transform shieldSpawnPoint;

    [Header("Weak Point Settings")]
    [Tooltip("この戦車の弱点(WeakPoint)に被弾した際の追加ダメージ量")]
    [SerializeField] private int weakPointBonusDamage = 10; // 倍率ではなく固定値加算

    // --- 内部パラメータ ---
    public int CurrentHp { get; private set; }
    public bool IsDead { get; private set; } = false;

    public int ActiveMineCount { get; set; } = 0;
    public int bonusBounces { get; private set; } = 0;
    public int bonusMaxAmmo { get; private set; } = 0;
    public float bonusMoveSpeed { get; private set; } = 0f;
    public float bonusShellSpeed { get; private set; } = 0f;
    public int bonusMineLimit { get; private set; } = 0;
    public float bonusRotationSpeed { get; private set; } = 0f;

    public bool IsInStun { get; set; } = false;
    private float _stunTimer = 0f;

    private ShieldController _currentShield;
    private ShieldController _activeShield;

    private void Start()
    {
        if (tankData != null)
        {
            CurrentHp = tankData.maxHp;
            if (tankData.startingShield != null) EquipShield(tankData.startingShield);
        }
        else
        {
            CurrentHp = 100;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterTank(this);
        }

        if (tankData != null && tankData.startingShield != null)
        {
            EquipShield(tankData.startingShield);
        }
    }

    private void Update()
    {
        if (IsInStun)
        {
            _stunTimer -= Time.deltaTime;
            if (_stunTimer <= 0f) IsInStun = false;
        }
    }
    // ★変更: 第2引数(amount)を追加（デフォルト0）
    public void ApplyPowerUp(ItemType type, float amount = 0f)
    {
        switch (type)
        {
            case ItemType.BouncePlus:
                bonusBounces++;
                break;

            case ItemType.MaxAmmoPlus:
                bonusMaxAmmo++;
                // 弾数が増えたことをコントローラーに通知
                SendMessage("OnMaxAmmoIncreased", SendMessageOptions.DontRequireReceiver);
                break;

            case ItemType.MoveSpeedUp:
                bonusMoveSpeed += amount; // ★インスペクターの値を使用
                break;

            case ItemType.ShellSpeedUp:
                bonusShellSpeed += amount; // ★インスペクターの値を使用
                break;

            case ItemType.MineLimitUp:
                bonusMineLimit++;
                break;

            case ItemType.TurnSpeedUp: // ★追加
                bonusRotationSpeed += amount;
                break;

            case ItemType.Shield: break;
            case ItemType.ChangeShell: break;
            case ItemType.ChangeMine: break;
        }
    }

    public void TakeDamage(int damage)
    {
        if (IsDead) return;

        // 試合終了後は無敵
        if (GameManager.Instance != null && GameManager.Instance.IsGameFinished()) return;

        if (_currentShield != null)
        {
            // シールド処理があれば記述
        }

        CurrentHp -= damage;

        if (CurrentHp <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (IsDead) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGameFinished()) return;

        IsDead = true;

        if (GameManager.Instance != null) GameManager.Instance.OnTankDead(this);

        // 自爆設定がある場合は自爆シーケンスへ
        if (tankData != null && tankData.isSelfDestruct)
        {
            StartCoroutine(SelfDestructSequence());
        }
        else
        {
            // 通常死亡
            StartCoroutine(PerformDeathSequence(false));
        }
    }

    // 自爆シーケンス
    private IEnumerator SelfDestructSequence()
    {
        // 1. 物理停止
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }

        // 2. 明滅処理 (selfDestructInterval の時間だけ待つように修正)
        // データがない場合はデフォルト2.0秒
        float duration = (tankData != null) ? tankData.selfDestructInterval : 2.0f;

        float timer = 0f;
        float flashSpeed = 0.1f;
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        while (timer < duration)
        {
            timer += flashSpeed;
            // 赤と白で点滅
            Color flashColor = (Mathf.FloorToInt(timer / flashSpeed) % 2 == 0) ? Color.red : Color.white;
            foreach (var r in renderers) if (r != null) r.material.color = flashColor;

            yield return new WaitForSeconds(flashSpeed);
        }

        // 3. 爆発ダメージ発生
        if (tankData != null && tankData.selfDestructDamage > 0 && tankData.selfDestructRadius > 0)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, tankData.selfDestructRadius);
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                TankStatus target = hit.GetComponentInParent<TankStatus>();
                if (target != null && !target.IsDead)
                {
                    target.TakeDamage(tankData.selfDestructDamage);
                }
            }

            // 自爆エフェクト
            GameObject boomPrefab = tankData.selfDestructExplosionPrefab;
            if (boomPrefab != null)
            {
                GameObject exp = Instantiate(boomPrefab, transform.position, Quaternion.identity);
                Destroy(exp, 3.0f);
            }
        }

        // 4. バラバラ処理へ
        StartCoroutine(PerformDeathSequence(true));
    }

    // 死亡演出（パーツ切り離し～完全消滅までを管理）
    private IEnumerator PerformDeathSequence(bool isSelfDestruct)
    {
        // --- A. 最初の爆発 ---
        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayExplosion(transform.position);
        }

        // --- B. パーツ切り離しと吹き飛ばし ---
        List<GameObject> activeParts = new List<GameObject>();

        if (partsToScatter != null && partsToScatter.Count > 0)
        {
            foreach (var part in partsToScatter)
            {
                if (part == null) continue;

                // 親子関係解除
                part.transform.SetParent(null);
                activeParts.Add(part);

                // コライダー設定
                Collider col = part.GetComponent<Collider>();
                if (col == null)
                {
                    MeshCollider mc = part.AddComponent<MeshCollider>();
                    mc.convex = true;
                    col = mc;
                }
                col.enabled = true;

                // 物理演算設定
                Rigidbody partRb = part.GetComponent<Rigidbody>();
                if (partRb == null) partRb = part.AddComponent<Rigidbody>();
                partRb.isKinematic = false;
                partRb.useGravity = true;

                // 吹き飛ばし (真上への力 + ランダム回転)
                partRb.AddForce(Vector3.up * 12f + Random.insideUnitSphere * 5f, ForceMode.Impulse);
                partRb.AddTorque(Random.insideUnitSphere * 1000f, ForceMode.Impulse);
            }
        }

        // --- C. 本体の無効化 (GameObjectは消さずに透明にする) ---
        // ※これをしないとコルーチンが止まってしまう

        // 本体のレンダラーを消す
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        // 本体のコライダーを消す
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

        // 本体の物理を消す
        Rigidbody mainRb = GetComponent<Rigidbody>();
        if (mainRb != null) mainRb.isKinematic = true;

        // --- D. パーツの爆破待ち ---
        // 各パーツについて、ランダムな時間後に爆発させる
        foreach (var part in activeParts)
        {
            if (part == null) continue;

            // 2〜3秒待つ（コルーチン内で個別に待機）
            StartCoroutine(ExplodeOnePart(part));
        }

        // 全てのパーツが消えるであろう時間（最大3秒 + マージン）だけ待つ
        yield return new WaitForSeconds(4.0f);

        // --- E. 最後に本体を削除 ---
        Destroy(gameObject);
    }

    // 個別のパーツを爆破して消す処理
    private IEnumerator ExplodeOnePart(GameObject part)
    {
        // ランダムな遅延
        float delay = Random.Range(1.5f, 3.0f);
        yield return new WaitForSeconds(delay);

        if (part != null)
        {
            // 爆発エフェクト生成
            GameObject effectPrefab = partExplosionEffect;
            if (effectPrefab == null && tankData != null) effectPrefab = tankData.selfDestructExplosionPrefab;

            if (effectPrefab != null)
            {
                GameObject exp = Instantiate(effectPrefab, part.transform.position, Quaternion.identity);
                // パーティクルを確実に消す
                Destroy(exp, 2.0f);
            }
            else if (EffectManager.Instance != null)
            {
                EffectManager.Instance.PlayExplosion(part.transform.position);
            }

            // パーツ削除
            Destroy(part);
        }
    }

    // --- 外部メソッド ---
    public void SetTeam(TeamType newTeam, bool asCaptain = false) { team = newTeam; isCaptain = asCaptain; }
    public void SetTeam(TeamType newTeam) => SetTeam(newTeam, false);

    public void EquipShield(ShieldData newShieldData)
    {
        if (newShieldData == null || newShieldData.prefab == null || shieldSpawnPoint == null) return;
        if (_activeShield != null) Destroy(_activeShield.gameObject);
        GameObject shieldObj = Instantiate(newShieldData.prefab, shieldSpawnPoint);
        shieldObj.transform.localPosition = Vector3.zero;
        shieldObj.transform.localRotation = Quaternion.identity;
        _activeShield = shieldObj.GetComponent<ShieldController>();
        if (_activeShield != null) _activeShield.Init(this, newShieldData);
    }

    public void ApplyPowerUp(ItemType type)
    {
        switch (type)
        {
            case ItemType.BouncePlus: bonusBounces++; break;
            case ItemType.MaxAmmoPlus: bonusMaxAmmo++; break;
            case ItemType.MoveSpeedUp: bonusMoveSpeed += 1.0f; break;

            case ItemType.ShellSpeedUp: bonusShellSpeed += 5.0f; break; // 例: 弾速+5
            case ItemType.MineLimitUp: bonusMineLimit++; break; // 例: 地雷上限+1

            case ItemType.Shield: break;
            case ItemType.ChangeShell: break;
            case ItemType.ChangeMine: break;
        }
    }

    // ★追加: Prefab変更用メソッド
    public void ChangeShellPrefab(GameObject newPrefab)
    {
        if (newPrefab != null) shellPrefab = newPrefab;
    }

    public void ChangeMinePrefab(GameObject newPrefab)
    {
        if (newPrefab != null) minePrefab = newPrefab;
    }

    public void OnShieldBroken() => _currentShield = null;
    public void OnMineExploded() => ActiveMineCount = Mathf.Max(0, ActiveMineCount - 1);
    public void OnMineRemoved() => ActiveMineCount = Mathf.Max(0, ActiveMineCount - 1);
    public void OnMinePlaced() => ActiveMineCount++;
    public void ApplyStun(float duration) { if (IsDead) return; IsInStun = true; _stunTimer = duration; }
    public void AddBonusBounces(int count) => bonusBounces += count;

    // --- ゲッター ---
    public TankStatusData GetData() => tankData;
    public float GetCurrentMoveSpeed()
    {
        float baseSpeed = tankData.moveSpeed + bonusMoveSpeed;
        if (_activeShield != null && _activeShield.Data != null) baseSpeed -= _activeShield.Data.speedPenalty;
        return Mathf.Max(1.0f, baseSpeed);
    }

    // ※射撃スクリプト側で tankData.maxAmmo の代わりにこのメソッドを使用してください
    public int GetTotalMaxAmmo()
    {
        // tankDataがnullでないことを確認
        int baseAmmo = (tankData != null) ? tankData.maxAmmo : 5;

        // 基本値 + アイテム取得数(bonusMaxAmmo) を返す
        return baseAmmo + bonusMaxAmmo;
    }

    // ★追加: 現在の跳弾回数を返す（ShellPrefabのデータ + ボーナス）
    public int GetTotalRicochetCount()
    {
        int baseCount = 0;
        if (shellPrefab != null)
        {
            var shell = shellPrefab.GetComponent<ShellController>();
            if (shell != null && shell.shellData != null)
            {
                baseCount = shell.shellData.maxBounces;
            }
        }
        return baseCount + bonusBounces;
    }

    // ★追加: 地雷設置上限
    public int GetTotalMineLimit()
    {
        int baseLimit = (tankData != null) ? tankData.maxMines : 3;
        return baseLimit + bonusMineLimit;
    }

    // ★変更: 弱点被弾時の処理
    // WeakPointスクリプトから呼ばれる
    public void TakeWeakPointDamage(int baseDamage)
    {
        // 基本ダメージ + 設定された追加ダメージ
        int totalDamage = baseDamage + weakPointBonusDamage;

        // ログ出力（デバッグ用）
        Debug.Log($"<color=red>[WeakPoint Hit!]</color> {name} takes {baseDamage} + {weakPointBonusDamage} = {totalDamage} damage!");

        // 通常のダメージ処理へ流す
        TakeDamage(totalDamage);
    }

    // ★追加: 現在の回転速度を計算して返す
    public float GetCurrentRotationSpeed()
    {
        float baseRot = (tankData != null) ? tankData.rotationSpeed : 90f;
        return baseRot + bonusRotationSpeed;
    }

    public GameObject GetShellPrefab() => shellPrefab;
    public GameObject GetMinePrefab() => minePrefab;
    public MineData GetMineData() => mineData;
}