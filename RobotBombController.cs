using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Linq;
using System.Collections.Generic;

public class RobotBombController : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField] private float rotationSpeed = 120.0f;
    [SerializeField] private float searchRadius = 20.0f;
    [SerializeField] private float explodeRadius = 3.0f;
    [SerializeField] private float lifeTime = 15.0f;
    [SerializeField] private float blinkDuration = 1.0f; // 明滅時間
    [SerializeField] private float blinkInterval = 0.1f; // 明滅間隔

    // ★追加: 地面に埋まる/浮くのを微調整するためのオフセット値
    // 埋まる場合はプラスの値（例: 0.5）、浮く場合はマイナスの値（例: -0.2）に設定してください
    [SerializeField] private float spawnHeightOffset = 0.0f;



    // ... (内部変数はそのまま) ...
    private TankStatus _ownerStatus;
    private MineData _mineData;
    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private TankStatus _target;
    private bool _isExploded = false;
    private bool _isBlinking = false;
    private Renderer _renderer; // ★追加: 点滅用

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
        // ★修正: 本体のRendererを取得（もしモデルが子にある場合は GetComponentInChildren<Renderer>() に変更）
        _renderer = GetComponent<Renderer>();
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
    }

    public void Init(TankStatus owner, MineData data)
    {
        _ownerStatus = owner;
        _mineData = data;

        // ★修正: Startを待たずに、ここでNavMeshAgentの設定を切る
        // これをしないと、Warpした瞬間にTransformが地面に吸着してしまう
        if (_agent != null)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }

        // NavMesh上の正確な地面の高さを取得
        UnityEngine.AI.NavMeshHit hit;
        if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out hit, 5.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            // 1. 論理上のエージェントは「地面」に配置する (Warp)
            if (_agent != null)
            {
                _agent.Warp(hit.position);
            }

            // 2. 見た目のオブジェクト(Transform)は「地面 + オフセット」の高さに配置する
            // これでエージェントと見た目の座標を切り離せます
            Vector3 visualPos = hit.position;
            visualPos.y += spawnHeightOffset;
            transform.position = visualPos;
        }
        else
        {
            // NavMeshが見つからなかった場合の保険
            transform.position += Vector3.up * spawnHeightOffset;
        }
    }

    // ... (GetTeam, OnSensorDetect はそのまま) ...
    public TeamType GetTeam() { return _ownerStatus != null ? _ownerStatus.team : TeamType.Neutral; }

    public void OnSensorDetect(Collider other)
    {
        if (_isExploded || _isBlinking) return;
        if (other.gameObject == gameObject) return;
        if (other.transform.IsChildOf(transform)) return;

        TankStatus detectedTank = other.GetComponentInParent<TankStatus>();
        if (detectedTank != null && _ownerStatus != null)
        {
            if (detectedTank.team != _ownerStatus.team && !detectedTank.IsDead)
            {
                _target = detectedTank;
            }
        }
    }

    private void Start()
    {
        if (_agent != null)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
            if (_agent.speed < 1.0f) _agent.speed = 3.5f;
        }

        // ★追加: 寿命管理用のコルーチンを開始
        StartCoroutine(LifeTimeRoutine());
    }

    // ★追加: 寿命が来たら明滅して爆発するコルーチン
    private IEnumerator LifeTimeRoutine()
    {
        // 寿命から明滅時間を引いた時間だけ待機
        // (例: 寿命15秒 - 明滅1秒 = 14秒待つ)
        float waitTime = Mathf.Max(0, lifeTime - blinkDuration);
        yield return new WaitForSeconds(waitTime);

        // まだ爆発していなければ明滅開始
        if (!_isExploded && !_isBlinking)
        {
            StartCoroutine(BlinkAndExplode());
        }
    }

    // ... (FixedUpdate, FindTarget, MoveTowardsTarget はそのまま) ...

    private void FixedUpdate()
    {
        if (_isExploded || _isBlinking) return; // 明滅中は動かない

        if (_target == null || _target.IsDead) FindTarget();

        if (_target != null)
        {
            MoveTowardsTarget();
            float dist = Vector3.Distance(transform.position, _target.transform.position);

            // ★修正: ターゲットに近づいた時も、同じ BlinkAndExplode を呼ぶ
            if (dist < 1.5f)
            {
                if (!_isBlinking) StartCoroutine(BlinkAndExplode());
            }
        }
        else
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.ResetPath();
            _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, Vector3.zero, 10f * Time.fixedDeltaTime);
        }
    }

    private void FindTarget()
    {
        if (_ownerStatus == null) return;
        var targets = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            .Where(t => t.team != _ownerStatus.team && !t.IsDead)
            .OrderBy(t => Vector3.Distance(transform.position, t.transform.position));
        _target = targets.FirstOrDefault();
    }

    private void MoveTowardsTarget()
    {
        if (_agent == null || !_agent.isOnNavMesh) return;
        _agent.SetDestination(_target.transform.position);

        Vector3 desiredVel = _agent.desiredVelocity;
        if (desiredVel.magnitude > 0.1f)
        {
            Vector3 moveDir = desiredVel.normalized;
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);

            Vector3 currentVel = _rb.linearVelocity;
            Vector3 targetVelVector = transform.forward * moveSpeed;
            Vector3 force = targetVelVector - currentVel;
            force.y = 0;
            _rb.AddForce(force * 10.0f, ForceMode.Acceleration);
        }
        _agent.nextPosition = _rb.position;
    }

    // --- 明滅ロジック ---
    private IEnumerator BlinkAndExplode()
    {
        // 多重起動防止
        if (_isBlinking || _isExploded) yield break;

        _isBlinking = true;
        _rb.linearVelocity = Vector3.zero; // 停止
        if (_agent != null) _agent.ResetPath();

        // 点滅処理
        float elapsed = 0f;
        bool isRed = false;
        Color originalColor = (_renderer != null) ? _renderer.material.color : Color.white;

        // 指定時間点滅を繰り返す
        while (elapsed < blinkDuration)
        {
            if (_renderer != null)
            {
                _renderer.material.color = isRed ? Color.red : originalColor;
            }
            isRed = !isRed; // 色を反転

            yield return new WaitForSeconds(blinkInterval);
            elapsed += blinkInterval;
        }

        // 色を戻して爆発
        if (_renderer != null) _renderer.material.color = originalColor;
        Explode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_isExploded) return;
        GameObject hitObj = collision.gameObject;

        if (hitObj.CompareTag("Wall") || hitObj.layer == LayerMask.NameToLayer("Wall")) return;
        if (hitObj == gameObject || hitObj.transform.IsChildOf(transform)) return;

        TankStatus hitTank = hitObj.GetComponentInParent<TankStatus>();
        if (hitTank != null)
        {
            if (_ownerStatus != null && hitTank.team == _ownerStatus.team) return;
            // 敵に接触したら「即」爆発（明滅なし）
            Explode();
        }
        else if (hitObj.CompareTag("Shell") || hitObj.CompareTag("Explosion") || gameObject.layer == LayerMask.NameToLayer("Explode"))
        {
            Explode();
        }
    }

    // ★変更: 爆発処理をMineControllerに合わせる
    public void Explode()
    {
        if (_isExploded) return;
        _isExploded = true;

        // コルーチン停止
        StopAllCoroutines();

        // 所有者へ通知（設置数カウント減など）
        if (_ownerStatus != null) _ownerStatus.OnMineExploded();

        // エフェクト生成（MineDataのエフェクトがあれば優先、なければManager経由）
        if (_mineData != null && _mineData.effectPrefab != null)
        {
            GameObject effect = Instantiate(_mineData.effectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 2.0f);
        }
        else if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayExplosion(transform.position);
        }

        // ダメージ適用
        ApplyExplosionDamage();

        // 本体削除
        Destroy(gameObject);
    }

    // ★変更: ダメージ適用処理をMineControllerと統一
    private void ApplyExplosionDamage()
    {
        // 爆発半径
        float radius = (_mineData != null) ? _mineData.explosionRadius : explodeRadius;
        int damage = (_mineData != null) ? _mineData.damage : 30;

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, radius);
        List<TankStatus> damagedTanks = new List<TankStatus>();

        foreach (var hit in hitColliders)
        {
            if (hit.gameObject == gameObject) continue;

            // 1. 戦車へのダメージ（重複防止）
            TankStatus tank = hit.GetComponentInParent<TankStatus>();
            if (tank != null && !damagedTanks.Contains(tank))
            {
                tank.TakeDamage(damage);
                damagedTanks.Add(tank);
            }

            // 2. 破壊可能ブロック
            DestructibleBlock block = hit.GetComponent<DestructibleBlock>();
            if (block != null) block.TakeDamage(damage);

            // 3. 他の爆発物（地雷・ロボボム）の誘爆
            // 自分自身は除外済み
            if (hit.CompareTag("Mine"))
            {
                MineController otherMine = hit.GetComponentInParent<MineController>();
                if (otherMine != null) otherMine.Explode();

                RobotBombController otherRobot = hit.GetComponentInParent<RobotBombController>();
                if (otherRobot != null && otherRobot != this) otherRobot.Explode();
            }

            // 4. 弾（爆風で消すなど）
            ShellController shell = hit.GetComponent<ShellController>();
            if (shell != null) shell.TriggerExplosionReaction();
        }
    }
    [Tooltip("トリガー(すり抜け判定)のオブジェクトと接触した際の処理")]
    private void OnTriggerEnter(Collider other)
    {
        if (_isExploded) return;
        GameObject hitObj = other.gameObject;

        // ★追加: 接触したのが戦車だった場合
        TankStatus hitTank = hitObj.GetComponentInParent<TankStatus>();
        if (hitTank != null)
        {
            // 味方チームでなければ即爆発
            if (_ownerStatus != null && hitTank.team != _ownerStatus.team)
            {
                Explode();
                return;
            }
        }

        // 弾や爆風に触れたら爆発
        if (hitObj.CompareTag("Shell") || hitObj.CompareTag("Explosion") || hitObj.layer == LayerMask.NameToLayer("Explode"))
        {
            Explode();
        }
    }
}