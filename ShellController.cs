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

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void Start()
    {
        CheckInsidePenetration();
    }

    private void FixedUpdate()
    {
        if (_isExploded) return;
        CheckInsidePenetration();

        if (_bounceCooldown > 0) _bounceCooldown -= Time.fixedDeltaTime;

        if (_rb.linearVelocity.magnitude < 1.0f && _lastVelocity.magnitude > 0)
        {
            HandleDestruction(null);
            return;
        }
        _lastVelocity = _rb.linearVelocity;
    }

    private void CheckInsidePenetration()
    {
        if (_isExploded) return;
        int insideLayerMask = LayerMask.GetMask("Inside");
        if (insideLayerMask != 0 && Physics.CheckSphere(transform.position, 0.1f, insideLayerMask))
        {
            Destroy(gameObject);
            _isExploded = true;
            return;
        }
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.1f);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("Inside"))
            {
                Destroy(gameObject);
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
            }
        }

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
        Destroy(gameObject, shellData.lifeTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_isExploded) return;
        GameObject hitObj = collision.collider.gameObject;

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
            Vector3 reflectDir = Vector3.Reflect(_lastVelocity.normalized, collision.contacts[0].normal);

            _rb.angularVelocity = Vector3.zero;
            transform.forward = reflectDir;

            // ★修正: 保存しておいた速度(_currentSpeed)を使用する
            _rb.linearVelocity = reflectDir * _currentSpeed;

            _lastVelocity = _rb.linearVelocity;

            if (_bounceCooldown <= 0)
            {
                if (_remainingBounces > 0)
                {
                    EffectManager.Instance.PlayWallHit(collision.contacts[0].point, -transform.forward);
                    EffectManager.Instance.RefrectionSound();
                    _remainingBounces--;
                    _bounceCooldown = 0.05f;
                }
                else HandleDestruction(collision);
            }
        }
        else HandleDestruction(collision);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isExploded) return;
        if (other.gameObject.layer == LayerMask.NameToLayer("Wall")) { CheckInsidePenetration(); return; }
        if (other.CompareTag("Inside") || other.gameObject.layer == LayerMask.NameToLayer("Inside")) { Destroy(gameObject); _isExploded = true; return; }
        if (other.CompareTag("Explosion") || other.gameObject.layer == LayerMask.NameToLayer("Explode")) { TriggerExplosionReaction(); return; }
        if (other.CompareTag("Mine") || other.CompareTag("Shell")) { HandleDestruction(null); }
    }

    public void TriggerExplosionReaction()
    {
        if (_isExploded) return;
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
        _isExploded = true;
        if (EffectManager.Instance != null)
        {
            if (shellData.isExplosive) { EffectManager.Instance.PlayExplosion(transform.position); ApplyExplosionDamage(); }
            else if (collision != null) { EffectManager.Instance.PlayStandardHit(collision.contacts[0].point, -transform.forward); ApplyDirectDamage(collision.collider.gameObject); }
        }
        EffectManager.Instance.ShootExplode();
        Destroy(gameObject);
    }

    private void ApplyDirectDamage(GameObject hitObject)
    {
        ShieldController shield = hitObject.GetComponent<ShieldController>();
        if (shield != null) { shield.TakeShieldDamage(shellData.damage); return; }
        WeakPoint weakPoint = hitObject.GetComponent<WeakPoint>();
        if (weakPoint != null) { weakPoint.TakeWeakPointDamage(shellData.damage); return; }
        TankStatus target = hitObject.GetComponentInParent<TankStatus>();
        if (target != null) target.TakeDamage(shellData.damage);
        if (hitObject.CompareTag("Mine")) hitObject.GetComponentInParent<MineController>()?.Explode();
    }

    private void ApplyExplosionDamage()
    {
        float radius = shellData.explosionRadius;
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius);
        HashSet<TankStatus> damagedBosses = new HashSet<TankStatus>();

        foreach (var hit in hitColliders)
        {
            if (hit.gameObject == gameObject) continue;
            ShellController otherShell = hit.GetComponent<ShellController>();
            if (otherShell != null) { otherShell.TriggerExplosionReaction(); continue; }

            WeakPoint wp = hit.GetComponent<WeakPoint>();
            if (wp != null && wp.bossStatus != null)
            {
                wp.TakeWeakPointDamage(shellData.damage);
                damagedBosses.Add(wp.bossStatus);
                continue;
            }
            TankStatus tank = hit.GetComponentInParent<TankStatus>();
            if (tank != null && !damagedBosses.Contains(tank))
            {
                tank.TakeDamage(shellData.damage);
                damagedBosses.Add(tank);
            }
            DestructibleBlock block = hit.GetComponent<DestructibleBlock>();
            if (block != null) block.TakeDamage(shellData.damage);
            if (hit.CompareTag("Mine")) hit.GetComponentInParent<MineController>()?.Explode();
        }
    }
}