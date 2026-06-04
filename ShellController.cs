using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ShellController : MonoBehaviour
{
    public ShellData shellData;
    public GameObject Owner { get; set; }

    private Rigidbody _rb;
    private int _remainingBounces;
    private Vector3 _lastVelocity;
    private bool _isExploded = false;
    private float _bounceCooldown = 0f;

    // ★追加: 現在の速度を保持する変数（跳弾時に速度低下しないため）
    private float _currentSpeed;
    private float _aliveTimer = 0f; // 経過時間
    private Vector3 _initialScale; // 初期スケール
    private Vector3 _flameMuzzleOrigin;

    // ★追加: 切り離したいパーティクルをインスペクターで登録するリスト
    [Header("Trail Settings")]
    public List<ParticleSystem> trailParticles = new List<ParticleSystem>();

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _initialScale = transform.localScale;
    }

    private void Start()
    {
        CheckInsidePenetration();
    }

    private void FixedUpdate()
    {
        if (_isExploded) return;
        CheckInsidePenetration();

        _aliveTimer += Time.fixedDeltaTime;

        if (_bounceCooldown > 0) _bounceCooldown -= Time.fixedDeltaTime;

        if (_rb.linearVelocity.magnitude < 1.0f && _lastVelocity.magnitude > 0)
        {
            HandleDestruction(null);
            return;
        }
        _lastVelocity = _rb.linearVelocity;

        // ★追加: 火炎放射弾のスケール拡大処理
        if (shellData != null && shellData.isFlamethrower && shellData.scaleOverTime)
        {
            float progress = Mathf.Clamp01(_aliveTimer / shellData.lifeTime);
            float currentMultiplier = Mathf.Lerp(1.0f, shellData.scaleMultiplier, progress);
            transform.localScale = _initialScale * currentMultiplier;
        }
    }

    private void CheckInsidePenetration()
    {
        if (_isExploded) return;
        int insideLayerMask = LayerMask.GetMask("Inside");
        if (insideLayerMask != 0 && Physics.CheckSphere(transform.position, 0.1f, insideLayerMask))
        {
            HandleDestruction(null); // ★修正: 直接Destroyせず共通処理へ
            _isExploded = true;
            return;
        }
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.1f);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("Inside"))
            {
                HandleDestruction(null); // ★修正: 直接Destroyせず共通処理へ
                _isExploded = true;
                return;
            }
        }
    }

    public void Launch(GameObject owner, int extraBounces = 0)
    {
        this.Owner = owner;
        _isExploded = false;
        CheckInsidePenetration();
        if (_isExploded) return;

        int statusBouncesBonus = 0;
        float statusSpeedBonus = 0f;

        if (owner != null)
        {
            TankStatus status = owner.GetComponent<TankStatus>();
            if (status != null)
            {
                statusBouncesBonus = status.bonusBounces;
                statusSpeedBonus = status.bonusShellSpeed;
                if (status.isDevilGiant)
                {
                    transform.localScale *= 1.5f;
                    _initialScale = transform.localScale;
                }
            }
        }

        SetupCollisionIgnores(owner);

        if (shellData.isFlamethrower)
        {
            _remainingBounces = 0;
            _flameMuzzleOrigin = transform.position;
        }
        else
            _remainingBounces = shellData.maxBounces + extraBounces + statusBouncesBonus;
        _rb = GetComponent<Rigidbody>();

        // ★修正: 速度を決定し、メンバ変数に保存
        _currentSpeed = shellData.speed + statusSpeedBonus;

        Vector3 launchVelocity = transform.forward * _currentSpeed;
        _rb.linearVelocity = launchVelocity;
        _lastVelocity = launchVelocity;

        if (shellData.isExplosive && DebugVisualizer.Instance != null)
        {
            DebugVisualizer.Instance.CreateVisualizer(transform, shellData.explosionRadius);
        }
        // ★修正: 自然消滅時もパーティクルを分離させるため、Invokeを使用
        Invoke(nameof(AutoDestroy), shellData.lifeTime);
    }

    private void AutoDestroy()
    {
        HandleDestruction(null);
    }

    // ==========================================
    // ★修正: 弾の跳弾処理（壁の隙間での連続スタック防止）
    // ==========================================
    private void OnCollisionEnter(Collision collision)
    {
        if (_isExploded) return;

        // ★追加: クールダウン中（0.05秒間）は、いかなる衝突判定も完全に無視してすり抜けるようにする（スタック防止）
        if (_bounceCooldown > 0) return;

        GameObject hitObj = collision.collider.gameObject;

        if (ShouldPassThrough(hitObj)) return;

        if (hitObj.GetComponent<WeakPoint>() != null ||
            hitObj.GetComponent<ShieldController>() != null ||
            hitObj.CompareTag("Tank") || hitObj.CompareTag("Mine") || hitObj.CompareTag("Shell"))
        {
            if (hitObj.CompareTag("Mine")) hitObj.GetComponentInParent<MineController>()?.Explode();

            HandleDestruction(collision);
            return;
        }

        if (hitObj.layer == LayerMask.NameToLayer("Wall") || hitObj.CompareTag("Wall"))
        {
            if (_remainingBounces > 0)
            {
                // 跳ね返りベクトルの計算
                Vector3 reflectDir = Vector3.Reflect(_lastVelocity.normalized, collision.contacts[0].normal);
                reflectDir.y = 0; // 高さは水平に保つ
                reflectDir.Normalize();

                _rb.angularVelocity = Vector3.zero;
                transform.forward = reflectDir;

                _rb.linearVelocity = reflectDir * _currentSpeed;
                _lastVelocity = _rb.linearVelocity;

                // ★追加: 壁の中にめり込んで連続ヒットしないよう、当たった壁の法線方向に少しだけ強制的に押し出す
                transform.position += collision.contacts[0].normal * 0.05f;

                EffectManager.Instance.PlayWallHit(collision.contacts[0].point, -transform.forward);
                EffectManager.Instance.RefrectionSound();

                _remainingBounces--;

                // ★修正: クールダウン時間をセット（この間は次の衝突判定を行わない）
                _bounceCooldown = 0.05f;
            }
            else
            {
                // 跳弾回数が0の場合は爆発（消滅）する
                HandleDestruction(collision);
            }
        }
        else
        {
            HandleDestruction(collision);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isExploded) return;
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall")) { CheckInsidePenetration(); return; }
        if (other.CompareTag("Inside") || other.gameObject.layer == LayerMask.NameToLayer("Inside")) { Destroy(gameObject); _isExploded = true; return; }
        if (ShouldPassThrough(other.gameObject)) return;
        if (other.CompareTag("Explosion") || other.gameObject.layer == LayerMask.NameToLayer("Explode")) { TriggerExplosionReaction(); return; }

        if (other.CompareTag("Tank"))
        {
            ApplyDirectDamage(other.gameObject);
            HandleDestruction(null);
            return;
        }

        if (other.CompareTag("Mine") || other.CompareTag("Shell"))
        {
            HandleDestruction(null);
        }
    }

    /// <summary>
    /// 火炎弾：発射体本体のみ貫通。味方・コアには当たってダメージ。他の火炎弾・爆発系は無視。
    /// </summary>
    private bool ShouldPassThrough(GameObject hitObj)
    {
        if (shellData == null || !shellData.ignoreExplosionsAndShells) return false;

        if (IsOwnerCollider(hitObj)) return true;

        ShellController otherShell = hitObj.GetComponent<ShellController>() ?? hitObj.GetComponentInParent<ShellController>();
        if (otherShell != null && otherShell != this && otherShell.shellData != null && otherShell.shellData.ignoreExplosionsAndShells)
            return true;

        if (hitObj.CompareTag("Explosion") || hitObj.gameObject.layer == LayerMask.NameToLayer("Explode"))
            return true;

        return false;
    }

    private bool IsOwnerCollider(GameObject hitObj)
    {
        if (Owner == null) return false;
        if (hitObj.transform.IsChildOf(Owner.transform)) return true;
        TankStatus ownerStatus = Owner.GetComponent<TankStatus>();
        TankStatus hitStatus = hitObj.GetComponentInParent<TankStatus>();
        return ownerStatus != null && hitStatus != null && hitStatus == ownerStatus;
    }

    private void SetupCollisionIgnores(GameObject owner)
    {
        if (shellData == null || !shellData.ignoreExplosionsAndShells) return;

        Collider[] myCols = GetComponentsInChildren<Collider>();
        if (owner != null)
        {
            foreach (var ownerCol in owner.GetComponentsInChildren<Collider>())
            {
                foreach (var myCol in myCols)
                {
                    if (ownerCol != null && myCol != null)
                        Physics.IgnoreCollision(myCol, ownerCol, true);
                }
            }
        }

        ShellController[] allShells = FindObjectsOfType<ShellController>();
        foreach (var other in allShells)
        {
            if (other == null || other == this || other._isExploded) continue;
            if (other.shellData == null || !other.shellData.ignoreExplosionsAndShells) continue;
            if (other.Owner != Owner && (owner == null || other.Owner != owner)) continue;

            Collider[] otherCols = other.GetComponentsInChildren<Collider>();
            foreach (var myCol in myCols)
            {
                foreach (var otherCol in otherCols)
                {
                    if (myCol != null && otherCol != null)
                        Physics.IgnoreCollision(myCol, otherCol, true);
                }
            }
        }
    }

    public void TriggerExplosionReaction()
    {
        if (_isExploded) return;

        // 火炎放射弾で、爆発を無視する設定なら何もしない
        if (shellData != null && shellData.isFlamethrower && shellData.ignoreExplosionsAndShells) return;

        // ★重要: Destroy前にパーティクルを分離
        DetachAndStopParticles();

        _isExploded = true;
        if (shellData.isExplosive)
        {
            if (EffectManager.Instance != null)
            {
                EffectManager.Instance.PlayExplosion(transform.position);
                ApplyExplosionDamage();
            }
        }
        Destroy(gameObject);
    }

    private void HandleDestruction(Collision collision)
    {
        if (_isExploded) return;

        // ★重要: Destroy前にパーティクルを分離
        DetachAndStopParticles();

        _isExploded = true;
        if (EffectManager.Instance != null)
        {
            if (shellData.isExplosive) { EffectManager.Instance.PlayExplosion(transform.position); ApplyExplosionDamage(); }
            else if (collision != null) { EffectManager.Instance.PlayStandardHit(collision.contacts[0].point, -transform.forward); ApplyDirectDamage(collision.collider.gameObject); }
        }

        // 消滅音の再生制御
        if (shellData == null || !shellData.muteDestroySound)
        {
            if (EffectManager.Instance != null)
            {
                EffectManager.Instance.ShootExplode();
            }
        }

        Destroy(gameObject);
    }

    private bool IsFlameLineBlockedByWall(Vector3 targetPos)
    {
        if (shellData == null || !shellData.isFlamethrower) return false;

        Vector3 start = _flameMuzzleOrigin + Vector3.up * 0.5f;
        Vector3 end = targetPos + Vector3.up * 0.5f;
        Vector3 delta = end - start;
        float dist = delta.magnitude;
        if (dist < 0.05f) return false;

        int wallMask = LayerMask.GetMask("Wall");
        return Physics.SphereCast(start, 0.3f, delta.normalized, out _, dist, wallMask);
    }

    private void ApplyDirectDamage(GameObject hitObject)
    {
        if (shellData != null && shellData.isFlamethrower && IsFlameLineBlockedByWall(hitObject.transform.position))
            return;

        TankStatus ownerTank = Owner != null ? Owner.GetComponent<TankStatus>() : null;

        // ★追加: アイテムボックスへの直撃ダメージ
        ItemBoxController itemBox = hitObject.GetComponent<ItemBoxController>();
        if (itemBox != null) { itemBox.TakeDamage(shellData.damage, ownerTank); return; }

        ShieldController shield = hitObject.GetComponent<ShieldController>();
        if (shield != null) { shield.TakeShieldDamage(shellData.damage); return; }

        WeakPoint weakPoint = hitObject.GetComponent<WeakPoint>();
        if (weakPoint != null) { weakPoint.TakeWeakPointDamage(shellData.damage, ownerTank); return; }

        DamageForwarder forwarder = hitObject.GetComponentInParent<DamageForwarder>();
        if (forwarder != null) { forwarder.mainTankStatus.TakeDamage(shellData.damage, ownerTank); return; }

        TankStatus target = hitObject.GetComponentInParent<TankStatus>();
        if (target != null) target.TakeDamage(shellData.damage, ownerTank);

        if (hitObject.CompareTag("Mine")) hitObject.GetComponentInParent<MineController>()?.Explode();
    }

    private void ApplyExplosionDamage()
    {
        float radius = shellData.explosionRadius;
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius);
        HashSet<TankStatus> damagedBosses = new HashSet<TankStatus>();

        TankStatus ownerTank = Owner != null ? Owner.GetComponent<TankStatus>() : null;

        foreach (var hit in hitColliders)
        {
            if (hit.gameObject == gameObject) continue;
            ShellController otherShell = hit.GetComponent<ShellController>();
            if (otherShell != null)
            {
                bool passThroughShell = shellData != null && shellData.ignoreExplosionsAndShells
                    && otherShell.shellData != null && otherShell.shellData.ignoreExplosionsAndShells;
                if (!passThroughShell) otherShell.TriggerExplosionReaction();
                continue;
            }

            // ★追加: アイテムボックスへの爆風ダメージ
            ItemBoxController itemBox = hit.GetComponent<ItemBoxController>();
            if (itemBox != null) itemBox.TakeDamage(shellData.damage, ownerTank);

            WeakPoint wp = hit.GetComponent<WeakPoint>();
            if (wp != null && wp.bossStatus != null)
            {
                wp.TakeWeakPointDamage(shellData.damage, ownerTank);
                damagedBosses.Add(wp.bossStatus);
                continue;
            }

            TankStatus tank = hit.GetComponentInParent<TankStatus>();
            if (tank == null)
            {
                DamageForwarder forwarder = hit.GetComponentInParent<DamageForwarder>();
                if (forwarder != null) tank = forwarder.mainTankStatus;
            }

            if (tank != null && !damagedBosses.Contains(tank))
            {
                tank.TakeDamage(shellData.damage, ownerTank);
                damagedBosses.Add(tank);
            }
            DestructibleBlock block = hit.GetComponent<DestructibleBlock>();
            if (block != null) block.TakeDamage(shellData.damage);
            if (hit.CompareTag("Mine")) hit.GetComponentInParent<MineController>()?.Explode();
        }
    }

    // ★追加: パーティクルを親から切り離して放出を止めるメソッド
    private void DetachAndStopParticles()
    {
        foreach (var ps in trailParticles)
        {
            if (ps != null)
            {
                ps.transform.parent = null; // 親子関係を解除
                ps.Stop();                  // 新規放出を停止

                // 放出済みの粒子が消えたら自動でGameObjectが消えるようにする設定（推奨）
                // または、ここで一定時間後に消去する
                Destroy(ps.gameObject, ps.main.duration + ps.main.startLifetime.constantMax);
            }
        }
    }

}