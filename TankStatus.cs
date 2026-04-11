using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class TankStatus : MonoBehaviour
{
    [Header("Data References")]
    [SerializeField] private TankStatusData tankData;

    [Header("Prefabs & Settings")]
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
    public BuffStepData buffData;

    public int CurrentHp { get; private set; }
    public bool IsDead { get; private set; } = false;
    public int ActiveMineCount { get; set; } = 0;

    public int levelBounces { get; private set; } = 0;
    public int levelMaxAmmo { get; private set; } = 0;
    public int levelMoveSpeed { get; private set; } = 0;
    public int levelShellSpeed { get; private set; } = 0;
    public int levelMineLimit { get; private set; } = 0;
    public int levelRotationSpeed { get; private set; } = 0;
    public int levelShellDamage { get; private set; } = 0;
    public int levelMineDamage { get; private set; } = 0;

    public bool isBerserkerMode { get; private set; } = false;

    // 暴走自爆モードフラグと、突撃方向の保存
    public bool isJammingBerserk { get; private set; } = false;
    public Vector3 berserkDirection { get; private set; } = Vector3.forward;

    public ShieldData currentShieldData;

    public bool IsInStun { get; set; } = false;
    private float _stunTimer = 0f;

    public bool IsJammed { get; private set; } = false;
    private float _jamTimer = 0f;

    public TankStatus LastAttacker { get; private set; }

    private ShieldController _currentShield;
    private ShieldController _activeShield;
    private Rigidbody _rb;

    // ★追加: 爆発シーケンス管理用
    public bool isBerserkExploding { get; private set; } = false;
    public float berserkTimer = 8.0f; // 暴走してからの制限時間
                                      // ★修正: 爆発シーケンス管理用
    private Renderer[] _berserkRenderers; // 点滅制御用


    // ★追加: 暴走時専用の加算速度を保存する変数
    private float _berserkBonusSpeedVal = 0f;



    private void Start()
    {
        _rb = GetComponent<Rigidbody>();

        if (tankData != null)
        {
            CurrentHp = tankData.maxHp;
            if (tankData.startingShield != null) EquipShield(tankData.startingShield);
        }
        else CurrentHp = 100;

        if (tankData != null && tankData.maxHp >= 10 && HPBarManager.Instance != null)
            HPBarManager.Instance.RegisterTank(this);

        if (GameManager.Instance != null) GameManager.Instance.RegisterTank(this);

        if (team == TeamType.Blue && GlobalGameManager.Instance != null && GlobalGameManager.Instance.isSimpleMode)
        {
            levelBounces = GlobalGameManager.Instance.savedLevelBounces;
            levelMaxAmmo = GlobalGameManager.Instance.savedLevelMaxAmmo;
            levelMoveSpeed = GlobalGameManager.Instance.savedLevelMoveSpeed;
            levelShellSpeed = GlobalGameManager.Instance.savedLevelShellSpeed;
            levelMineLimit = GlobalGameManager.Instance.savedLevelMineLimit;
            levelRotationSpeed = GlobalGameManager.Instance.savedLevelRotationSpeed;
            levelShellDamage = GlobalGameManager.Instance.savedLevelShellDamage;
            levelMineDamage = GlobalGameManager.Instance.savedLevelMineDamage;
            isBerserkerMode = GlobalGameManager.Instance.savedIsBerserker;

            if (GlobalGameManager.Instance.savedShellPrefab != null) shellPrefab = GlobalGameManager.Instance.savedShellPrefab;
            if (GlobalGameManager.Instance.savedMinePrefab != null) minePrefab = GlobalGameManager.Instance.savedMinePrefab;
            if (GlobalGameManager.Instance.savedShieldData != null) EquipShield(GlobalGameManager.Instance.savedShieldData);
        }
    }

    private void Update()
    {
        if (IsInStun)
        {
            _stunTimer -= Time.deltaTime;
            if (_stunTimer <= 0f) IsInStun = false;
        }

        if (IsJammed)
        {
            _jamTimer -= Time.deltaTime;
            if (_jamTimer <= 0f) IsJammed = false;
        }

        // ★修正: 暴走中のタイマー進行と、全期間を通した明滅処理
        if (isJammingBerserk && !IsDead)
        {
            berserkTimer -= Time.deltaTime;

            if (berserkTimer > 0f)
            {
                float blinkSpeed;
                if (berserkTimer > 1.0f)
                {
                    // 残り1秒より前は「遅めの明滅」をずっと続ける
                    blinkSpeed = 5.0f;
                }
                else
                {
                    // 残り1秒を切ったら「激しい明滅」に一気に加速する
                    float progress = 1.0f - berserkTimer;
                    blinkSpeed = Mathf.Lerp(15.0f, 40.0f, progress);
                }

                // Time.time を使って途切れない滑らかな明滅を計算
                float intensity = Mathf.Abs(Mathf.Sin(Time.time * blinkSpeed));
                Color warningColor = new Color(3.0f, 0.5f, 0.0f); // 地雷と同じオレンジ
                Color emissionColor = Color.Lerp(Color.black, warningColor, intensity);

                if (_berserkRenderers != null)
                {
                    foreach (var r in _berserkRenderers)
                    {
                        if (r != null)
                        {
                            foreach (var mat in r.materials)
                            {
                                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                            }
                        }
                    }
                }
            }
            else
            {
                // 8秒経過でタイムアウト強制自爆
                CurrentHp = 0;
                Die();
            }
        }
    }

    // ジャミング波に触れた瞬間、暴走スタート
    public void ActivateJammingBerserk(float bonusSpeed)
    {
        if (IsDead || isJammingBerserk) return;

        isJammingBerserk = true;
        berserkTimer = 8.0f;        // 8秒後に自動爆発

        IsJammed = false;
        IsInStun = false;

        _berserkBonusSpeedVal = bonusSpeed;

        Vector3 dir = Vector3.forward;
        if (transform != null) dir = transform.forward;

        dir.y = 0;
        berserkDirection = dir.normalized;

        // 突っ込んでくるときにポイントライトを赤色にする
        foreach (var light in GetComponentsInChildren<Light>())
        {
            if (light != null && light.type == LightType.Point)
            {
                light.color = Color.red;
            }
        }

        // マテリアルのEmissionを準備しておく
        _berserkRenderers = GetComponentsInChildren<Renderer>();
        if (_berserkRenderers != null)
        {
            foreach (var r in _berserkRenderers)
            {
                if (r != null)
                {
                    foreach (var mat in r.materials) mat.EnableKeyword("_EMISSION");
                }
            }
        }
    }

    // ★修正: 暴走中は「敵に触れた瞬間」に即自爆する
    private void OnCollisionEnter(Collision collision)
    {
        if (!isJammingBerserk || IsDead) return;

        TankStatus otherTank = collision.gameObject.GetComponentInParent<TankStatus>();
        if (otherTank != null && otherTank.team != this.team && !otherTank.IsDead)
        {
            CurrentHp = 0;
            Die();
        }
    }

    // トリガー（センサーなど）に触れた場合
    private void OnTriggerEnter(Collider other)
    {
        if (!isJammingBerserk || IsDead) return;

        TankStatus otherTank = other.GetComponentInParent<TankStatus>();
        if (otherTank != null && otherTank.team != this.team && !otherTank.IsDead)
        {
            CurrentHp = 0;
            Die();
        }
    }
    public void ApplyPowerUp(ItemType type)
    {
        switch (type)
        {
            case ItemType.BouncePlus: if (levelBounces < 5) levelBounces++; break;
            case ItemType.MaxAmmoPlus:
                if (levelMaxAmmo < 3)
                {
                    levelMaxAmmo++;
                    SendMessage("OnMaxAmmoIncreased", SendMessageOptions.DontRequireReceiver);
                }
                break;
            case ItemType.MoveSpeedUp: if (levelMoveSpeed < 5) levelMoveSpeed++; break;
            case ItemType.ShellSpeedUp: if (levelShellSpeed < 5) levelShellSpeed++; break;
            case ItemType.TurnSpeedUp: if (levelRotationSpeed < 5) levelRotationSpeed++; break;
            case ItemType.MineLimitUp: if (levelMineLimit < 5) levelMineLimit++; break;
            case ItemType.ShellDamageUp: if (levelShellDamage < 10) levelShellDamage++; break;
            case ItemType.MineDamageUp: if (levelMineDamage < 10) levelMineDamage++; break;
            case ItemType.ExtraLife:
                if (GlobalGameManager.Instance != null) GlobalGameManager.Instance.playerLives++;
                break;
            case ItemType.BerserkerMode:
                isBerserkerMode = !isBerserkerMode;
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
        if (GlobalGameManager.Instance != null && GlobalGameManager.Instance.SelectedStage != null)
            if (GlobalGameManager.Instance.SelectedStage.isInvincibleStage) return;

        if (attacker != null) LastAttacker = attacker;
        if (_currentShield != null) return;

        CurrentHp -= damage;
        if (HPBarManager.Instance != null) HPBarManager.Instance.UpdateHP(this, CurrentHp, tankData != null ? tankData.maxHp : 100);

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

        if (isJammingBerserk || (tankData != null && tankData.isSelfDestruct))
            StartCoroutine(SelfDestructSequence());
        else
            StartCoroutine(PerformDeathSequence(false));
    }

    private IEnumerator SelfDestructSequence()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; }

        // ★追加: 死亡時の自爆前合図として、ポイントライトを赤色にする
        foreach (var light in GetComponentsInChildren<Light>())
        {
            if (light != null && light.type == LightType.Point)
            {
                light.color = Color.red;
            }
        }

        float duration = (tankData != null) ? tankData.selfDestructInterval : 2.0f;

        // すでに暴走特攻で点滅が終わっている場合は一瞬で爆発する
        if (isJammingBerserk) duration = 0.1f;

        float timer = 0f;
        Color warningColor = new Color(3.0f, 0.5f, 0.0f); // ★地雷と同じオレンジ色のEmission
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        // 全マテリアルのEmissionを有効化する
        foreach (var r in renderers)
        {
            if (r != null)
            {
                foreach (var mat in r.materials) mat.EnableKeyword("_EMISSION");
            }
        }

        // ★地雷と完全に同じ加速点滅アルゴリズム
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float progress = timer / duration;
            float blinkSpeed = Mathf.Lerp(2.0f, 10.0f, progress);
            float intensity = Mathf.Abs(Mathf.Sin(timer * blinkSpeed));
            Color emissionColor = Color.Lerp(Color.black, warningColor, intensity);

            foreach (var r in renderers)
            {
                if (r != null)
                {
                    foreach (var mat in r.materials)
                    {
                        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                    }
                }
            }
            yield return null;
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
            else if (EffectManager.Instance != null) EffectManager.Instance.PlayExplosion(part.transform.position);
            Destroy(part);
        }
    }

    public void SetTeam(TeamType newTeam, bool asCaptain = false, bool asBoss = false, int spawnIdx = -1)
    {
        team = newTeam; isCaptain = asCaptain; isBoss = asBoss; spawnIndex = spawnIdx;
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
        currentShieldData = newShieldData;
    }

    public void ChangeShellPrefab(GameObject newPrefab) { if (newPrefab != null) shellPrefab = newPrefab; }
    public void ChangeMinePrefab(GameObject newPrefab) { if (newPrefab != null) minePrefab = newPrefab; }

    public void OnShieldBroken() => _currentShield = null;
    public void OnMineExploded() => ActiveMineCount = Mathf.Max(0, ActiveMineCount - 1);
    public void OnMineRemoved() => ActiveMineCount = Mathf.Max(0, ActiveMineCount - 1);
    public void OnMinePlaced() => ActiveMineCount++;
    public void ApplyStun(float duration) { if (IsDead) return; IsInStun = true; _stunTimer = duration; }

    public void ApplyJamming(float duration) { if (IsDead) return; IsJammed = true; _jamTimer = duration; }

    public TankStatusData GetData() => tankData;

    public float GetCurrentMoveSpeed()
    {
        if (IsJammed && !isJammingBerserk) return 0f;

        float baseSpeed = tankData != null ? tankData.moveSpeed : 5.0f;
        baseSpeed += bonusMoveSpeed;
        if (_activeShield != null && _activeShield.Data != null) baseSpeed -= _activeShield.Data.speedPenalty;

        // ★修正: 暴走中は基本速度を2倍にした上で、アンテナ戦車ごとに指定された「ボーナス速度」を丸ごと上乗せする
        if (isJammingBerserk)
        {
            baseSpeed = (baseSpeed * 2.0f) + _berserkBonusSpeedVal;
        }
        else if (isBerserkerMode)
        {
            baseSpeed *= 2.0f;
        }

        return Mathf.Max(1.0f, baseSpeed);
    }

    public float GetCurrentRotationSpeed()
    {
        if (IsJammed && !isJammingBerserk) return 0f;
        return ((tankData != null) ? tankData.rotationSpeed : 90f) + bonusRotationSpeed;
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

    public int bonusBounces => buffData != null ? buffData.bounceBonus[Mathf.Min(levelBounces, buffData.bounceBonus.Length - 1)] : levelBounces;
    public int bonusMaxAmmo => buffData != null ? buffData.maxAmmoBonus[Mathf.Min(levelMaxAmmo, buffData.maxAmmoBonus.Length - 1)] : levelMaxAmmo;
    public float bonusMoveSpeed => buffData != null ? buffData.moveSpeedBonus[Mathf.Min(levelMoveSpeed, buffData.moveSpeedBonus.Length - 1)] : levelMoveSpeed * 1.0f;
    public float bonusShellSpeed => buffData != null ? buffData.shellSpeedBonus[Mathf.Min(levelShellSpeed, buffData.shellSpeedBonus.Length - 1)] : levelShellSpeed * 5.0f;
    public int bonusMineLimit => buffData != null ? buffData.mineLimitBonus[Mathf.Min(levelMineLimit, buffData.mineLimitBonus.Length - 1)] : levelMineLimit;
    public float bonusRotationSpeed => buffData != null ? buffData.rotationSpeedBonus[Mathf.Min(levelRotationSpeed, buffData.rotationSpeedBonus.Length - 1)] : levelRotationSpeed * 20.0f;

    // ★追加: 一定時間経過、または敵に近づいたら地雷のように点滅して自爆する処理
    public void StartBerserkExplosionSequence()
    {
        if (isBerserkExploding || IsDead) return;
        isBerserkExploding = true;

        StartCoroutine(BerserkBlinkAndExplode());
    }

    private IEnumerator BerserkBlinkAndExplode()
    {
        float blinkDuration = 1.0f; // 1秒間点滅する
        float timer = 0f;
        Color warningColor = new Color(3.0f, 0.5f, 0.0f); // ★地雷と同じオレンジ色のEmission
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        // 全マテリアルのEmissionを有効化する
        foreach (var r in renderers)
        {
            if (r != null)
            {
                foreach (var mat in r.materials) mat.EnableKeyword("_EMISSION");
            }
        }

        // ★地雷と完全に同じ加速点滅アルゴリズム
        while (timer < blinkDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / blinkDuration;
            float blinkSpeed = Mathf.Lerp(2.0f, 10.0f, progress);
            float intensity = Mathf.Abs(Mathf.Sin(timer * blinkSpeed));
            Color emissionColor = Color.Lerp(Color.black, warningColor, intensity);

            foreach (var r in renderers)
            {
                if (r != null)
                {
                    foreach (var mat in r.materials)
                    {
                        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
                    }
                }
            }
            yield return null;
        }

        // 点滅が終わったらHPを0にして強制自爆
        CurrentHp = 0;
        Die();
    }

}