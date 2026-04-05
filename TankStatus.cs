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
    [SerializeField] private List<GameObject> partsToScatter;
    [SerializeField] private GameObject partExplosionEffect;

    [Header("Game Rule Settings")]
    public TeamType team;
    public bool isCaptain = false;
    public bool isBoss = false;
    public int spawnIndex = -1;

    [Header("Shield Configuration")]
    [SerializeField] private Transform shieldSpawnPoint;

    [Header("Weak Point Settings")]
    [SerializeField] private int weakPointBonusDamage = 10;

    [Header("Buff Data")]
    [Tooltip("レベルごとの上がり幅を設定したデータを入れます")]
    public BuffStepData buffData;

    // --- 内部パラメータ ---
    public int CurrentHp { get; private set; }
    public bool IsDead { get; private set; } = false;
    public int ActiveMineCount { get; set; } = 0;

    // 各ステータスのレベル
    public int levelBounces { get; private set; } = 0; // 上限5
    public int levelMaxAmmo { get; private set; } = 0; // ★上限3に変更
    public int levelMoveSpeed { get; private set; } = 0;
    public int levelShellSpeed { get; private set; } = 0;
    public int levelMineLimit { get; private set; } = 0;
    public int levelRotationSpeed { get; private set; } = 0;
    public int levelShellDamage { get; private set; } = 0; // 上限10
    public int levelMineDamage { get; private set; } = 0;  // 上限10

    public bool IsInStun { get; set; } = false;
    private float _stunTimer = 0f;
    public TankStatus LastAttacker { get; private set; }

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

        if (tankData != null && tankData.maxHp >= 10)
        {
            if (HPBarManager.Instance != null) HPBarManager.Instance.RegisterTank(this);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterTank(this);
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

    public void ApplyPowerUp(ItemType type)
    {
        switch (type)
        {
            case ItemType.BouncePlus:
                if (levelBounces < 5) levelBounces++;
                break;
            case ItemType.MaxAmmoPlus:
                // ★最大弾数の上限を3に修正
                if (levelMaxAmmo < 3)
                {
                    levelMaxAmmo++;
                    SendMessage("OnMaxAmmoIncreased", SendMessageOptions.DontRequireReceiver);
                }
                break;
            case ItemType.MoveSpeedUp:
                if (levelMoveSpeed < 5) levelMoveSpeed++;
                break;
            case ItemType.ShellSpeedUp:
                if (levelShellSpeed < 5) levelShellSpeed++;
                break;
            case ItemType.TurnSpeedUp:
                if (levelRotationSpeed < 5) levelRotationSpeed++;
                break;
            case ItemType.MineLimitUp:
                if (levelMineLimit < 5) levelMineLimit++;
                break;
            case ItemType.ShellDamageUp:
                if (levelShellDamage < 10) levelShellDamage++;
                break;
            case ItemType.MineDamageUp:
                if (levelMineDamage < 10) levelMineDamage++;
                break;

            case ItemType.Shield: break;
            case ItemType.ChangeShell: break;
            case ItemType.ChangeMine: break;
        }
    }

    public void TakeDamage(int damage, TankStatus attacker = null)
    {
        if (IsDead) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGameFinished()) return;

        if (attacker != null) LastAttacker = attacker;

        if (_currentShield != null) return;

        CurrentHp -= damage;

        if (HPBarManager.Instance != null)
        {
            HPBarManager.Instance.UpdateHP(this, CurrentHp, tankData != null ? tankData.maxHp : 100);
        }

        if (CurrentHp <= 0) Die();
    }

    private void Die()
    {
        if (IsDead) return;
        if (GameManager.Instance != null && GameManager.Instance.IsGameFinished()) return;

        IsDead = true;

        if (GameManager.Instance != null) GameManager.Instance.OnTankDead(this);

        if (GlobalGameManager.Instance != null && team != TeamType.Blue && spawnIndex >= 0)
        {
            GlobalGameManager.Instance.RecordDefeat(spawnIndex);
        }

        if (tankData != null && tankData.isSelfDestruct)
        {
            StartCoroutine(SelfDestructSequence());
        }
        else
        {
            StartCoroutine(PerformDeathSequence(false));
        }
    }

    private IEnumerator SelfDestructSequence()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }

        float duration = (tankData != null) ? tankData.selfDestructInterval : 2.0f;
        float timer = 0f;
        float flashSpeed = 0.1f;
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        while (timer < duration)
        {
            timer += flashSpeed;
            Color flashColor = (Mathf.FloorToInt(timer / flashSpeed) % 2 == 0) ? Color.red : Color.white;
            foreach (var r in renderers) if (r != null) r.material.color = flashColor;
            yield return new WaitForSeconds(flashSpeed);
        }

        if (tankData != null && tankData.selfDestructDamage > 0 && tankData.selfDestructRadius > 0)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, tankData.selfDestructRadius);
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                TankStatus target = hit.GetComponentInParent<TankStatus>();
                if (target != null && !target.IsDead) target.TakeDamage(tankData.selfDestructDamage, this);
            }

            if (tankData.selfDestructExplosionPrefab != null)
            {
                GameObject exp = Instantiate(tankData.selfDestructExplosionPrefab, transform.position, Quaternion.identity);
                Destroy(exp, 3.0f);
            }
        }

        StartCoroutine(PerformDeathSequence(true));
    }

    private IEnumerator PerformDeathSequence(bool isSelfDestruct)
    {
        if (EffectManager.Instance != null) EffectManager.Instance.PlayExplosion(transform.position);

        List<GameObject> activeParts = new List<GameObject>();

        if (partsToScatter != null && partsToScatter.Count > 0)
        {
            foreach (var part in partsToScatter)
            {
                if (part == null) continue;
                part.transform.SetParent(null);
                activeParts.Add(part);

                Collider col = part.GetComponent<Collider>();
                if (col == null)
                {
                    MeshCollider mc = part.AddComponent<MeshCollider>();
                    mc.convex = true;
                    col = mc;
                }
                col.enabled = true;

                Rigidbody partRb = part.GetComponent<Rigidbody>();
                if (partRb == null) partRb = part.AddComponent<Rigidbody>();
                partRb.isKinematic = false;
                partRb.useGravity = true;

                partRb.AddForce(Vector3.up * 12f + Random.insideUnitSphere * 5f, ForceMode.Impulse);
                partRb.AddTorque(Random.insideUnitSphere * 1000f, ForceMode.Impulse);
            }
        }

        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

        Rigidbody mainRb = GetComponent<Rigidbody>();
        if (mainRb != null) mainRb.isKinematic = true;

        foreach (var part in activeParts)
        {
            if (part == null) continue;
            StartCoroutine(ExplodeOnePart(part));
        }

        yield return new WaitForSeconds(4.0f);
        Destroy(gameObject);
    }

    private IEnumerator ExplodeOnePart(GameObject part)
    {
        float delay = Random.Range(1.5f, 3.0f);
        yield return new WaitForSeconds(delay);

        if (part != null)
        {
            GameObject effectPrefab = partExplosionEffect;
            if (effectPrefab == null && tankData != null) effectPrefab = tankData.selfDestructExplosionPrefab;

            if (effectPrefab != null)
            {
                GameObject exp = Instantiate(effectPrefab, part.transform.position, Quaternion.identity);
                Destroy(exp, 2.0f);
            }
            else if (EffectManager.Instance != null)
            {
                EffectManager.Instance.PlayExplosion(part.transform.position);
            }
            Destroy(part);
        }
    }

    public void SetTeam(TeamType newTeam, bool asCaptain = false, bool asBoss = false, int spawnIdx = -1)
    {
        team = newTeam;
        isCaptain = asCaptain;
        isBoss = asBoss;
        spawnIndex = spawnIdx;
    }

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

    public void ChangeShellPrefab(GameObject newPrefab) { if (newPrefab != null) shellPrefab = newPrefab; }
    public void ChangeMinePrefab(GameObject newPrefab) { if (newPrefab != null) minePrefab = newPrefab; }

    public void OnShieldBroken() => _currentShield = null;
    public void OnMineExploded() => ActiveMineCount = Mathf.Max(0, ActiveMineCount - 1);
    public void OnMineRemoved() => ActiveMineCount = Mathf.Max(0, ActiveMineCount - 1);
    public void OnMinePlaced() => ActiveMineCount++;
    public void ApplyStun(float duration) { if (IsDead) return; IsInStun = true; _stunTimer = duration; }

    public TankStatusData GetData() => tankData;

    public float GetCurrentMoveSpeed()
    {
        float baseSpeed = tankData != null ? tankData.moveSpeed : 5.0f;
        baseSpeed += bonusMoveSpeed;
        if (_activeShield != null && _activeShield.Data != null) baseSpeed -= _activeShield.Data.speedPenalty;
        return Mathf.Max(1.0f, baseSpeed);
    }

    public int GetTotalMaxAmmo() => ((tankData != null) ? tankData.maxAmmo : 5) + bonusMaxAmmo;

    public int GetTotalRicochetCount()
    {
        int baseCount = 0;
        if (shellPrefab != null)
        {
            var shell = shellPrefab.GetComponent<ShellController>();
            if (shell != null && shell.shellData != null) baseCount = shell.shellData.maxBounces;
        }
        return baseCount + bonusBounces;
    }

    public int GetTotalMineLimit() => ((tankData != null) ? tankData.maxMines : 3) + bonusMineLimit;

    public float GetCurrentRotationSpeed() => ((tankData != null) ? tankData.rotationSpeed : 90f) + bonusRotationSpeed;

    public int GetTotalShellDamage(int baseDamage) => baseDamage + (buffData != null ? buffData.shellDamageBonus[Mathf.Min(levelShellDamage, buffData.shellDamageBonus.Length - 1)] : levelShellDamage * 10);
    public int GetTotalMineDamage(int baseDamage) => baseDamage + (buffData != null ? buffData.mineDamageBonus[Mathf.Min(levelMineDamage, buffData.mineDamageBonus.Length - 1)] : levelMineDamage * 20);

    public void TakeWeakPointDamage(int baseDamage, TankStatus attacker = null)
    {
        int totalDamage = baseDamage + weakPointBonusDamage;
        TakeDamage(totalDamage, attacker);
    }

    public GameObject GetShellPrefab() => shellPrefab;
    public GameObject GetMinePrefab() => minePrefab;
    public MineData GetMineData() => mineData;

    // =========================================================
    // 他のスクリプトからの参照エラーを防ぐ互換用プロパティ（BuffStepData参照）
    // =========================================================
    public int bonusBounces => buffData != null ? buffData.bounceBonus[Mathf.Min(levelBounces, buffData.bounceBonus.Length - 1)] : levelBounces;
    public int bonusMaxAmmo => buffData != null ? buffData.maxAmmoBonus[Mathf.Min(levelMaxAmmo, buffData.maxAmmoBonus.Length - 1)] : levelMaxAmmo;
    public float bonusMoveSpeed => buffData != null ? buffData.moveSpeedBonus[Mathf.Min(levelMoveSpeed, buffData.moveSpeedBonus.Length - 1)] : levelMoveSpeed * 1.0f;
    public float bonusShellSpeed => buffData != null ? buffData.shellSpeedBonus[Mathf.Min(levelShellSpeed, buffData.shellSpeedBonus.Length - 1)] : levelShellSpeed * 5.0f;
    public int bonusMineLimit => buffData != null ? buffData.mineLimitBonus[Mathf.Min(levelMineLimit, buffData.mineLimitBonus.Length - 1)] : levelMineLimit;
    public float bonusRotationSpeed => buffData != null ? buffData.rotationSpeedBonus[Mathf.Min(levelRotationSpeed, buffData.rotationSpeedBonus.Length - 1)] : levelRotationSpeed * 20.0f;
}