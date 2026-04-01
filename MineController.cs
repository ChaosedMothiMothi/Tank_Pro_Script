using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MineController : MonoBehaviour
{
    public MineData mineData;
    private TankStatus _ownerStatus;
    private bool _isExploded = false;

    private Renderer[] _renderers;
    private Color _warningColor = new Color(3.0f, 0.5f, 0.0f);

    private void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.linearDamping = 2.0f;
        }
        _renderers = GetComponentsInChildren<Renderer>();
    }

    public TeamType GetTeam()
    {
        return _ownerStatus != null ? _ownerStatus.team : TeamType.Neutral;
    }

    public void Init(TankStatus owner, MineData data)
    {
        _ownerStatus = owner;
        mineData = data;

        if (DebugVisualizer.Instance != null && mineData != null)
        {
            DebugVisualizer.Instance.CreateVisualizer(transform, mineData.explosionRadius);
        }

        StartCoroutine(AutoExplosionRoutine());
    }

    private IEnumerator AutoExplosionRoutine()
    {
        float totalTime = mineData.explosionDelay;
        float countdownTime = 3.0f;
        float waitTime = Mathf.Max(0f, totalTime - countdownTime);

        yield return new WaitForSeconds(waitTime);

        float timer = 0f;
        EnableEmission();

        while (timer < countdownTime)
        {
            if (_isExploded) yield break;
            timer += Time.deltaTime;
            float progress = timer / countdownTime;
            float blinkSpeed = Mathf.Lerp(2.0f, 10.0f, progress);
            float intensity = Mathf.Abs(Mathf.Sin(timer * blinkSpeed));
            Color emissionColor = Color.Lerp(Color.black, _warningColor, intensity);
            SetEmissionColor(emissionColor);
            yield return null;
        }

        Explode();
    }

    public void NotifySensorImpact(GameObject target)
    {
        if (_isExploded) return;

        if (target.CompareTag("Tank"))
        {
            TankStatus targetTank = target.GetComponentInParent<TankStatus>();
            if (_ownerStatus != null && targetTank != null)
            {
                if (targetTank.team == _ownerStatus.team) return;
            }
            Explode();
        }
        else if (target.CompareTag("Shell") || target.CompareTag("Explosion") ||
target.gameObject.layer == LayerMask.NameToLayer("Explode"))
        {
            Explode();
        }
    }

    public void Explode()
    {
        if (_isExploded) return;
        _isExploded = true;

        if (_ownerStatus != null) _ownerStatus.OnMineExploded();

        // エフェクト発生
        if (mineData != null && mineData.effectPrefab != null)
        {
            GameObject effect = Instantiate(mineData.effectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 2.0f);
        }
        else if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayExplosion(transform.position);
        }

        // 遅延なしでダメージ適用
        ApplyExplosionDamage();

        Destroy(gameObject);
    }

    private void ApplyExplosionDamage()
    {
        if (mineData == null) return;

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, mineData.explosionRadius);
        List<TankStatus> damagedTanks = new List<TankStatus>();

        foreach (var hit in hitColliders)
        {
            if (hit.gameObject == gameObject) continue;

            TankStatus tank = hit.GetComponentInParent<TankStatus>();
            if (tank != null && !damagedTanks.Contains(tank))
            {
                tank.TakeDamage(mineData.damage);
                damagedTanks.Add(tank);
            }

            DestructibleBlock block = hit.GetComponent<DestructibleBlock>();
            if (block != null) block.TakeDamage(mineData.damage);

            if (hit.CompareTag("Mine"))
            {
                MineController otherMine = hit.GetComponentInParent<MineController>();
                if (otherMine != null && otherMine != this) otherMine.Explode();
            }

            ShellController shell = hit.GetComponent<ShellController>();
            if (shell != null) shell.TriggerExplosionReaction();
        }
    }

    private void EnableEmission()
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
        {
            foreach (var mat in r.materials) mat.EnableKeyword("_EMISSION");
        }
    }

    private void SetEmissionColor(Color color)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
        {
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color);
            }
        }
    }
}