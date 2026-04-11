using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class AntennaBossController : MonoBehaviour
{
    [Header("Settings")]
    public EnemyData enemyData;
    public TankStatus tankStatus;
    public Transform turretTransform;
    public Transform firePoint;
    public GameObject minePrefab;

    [Header("--- Boss: Burst Fire Settings ---")]
    [Tooltip("一度の攻撃で連射する弾の数")]
    public int burstCount = 3;
    [Tooltip("連射時の弾と弾の間隔（秒）")]
    public float burstInterval = 0.15f;

    [Tooltip("【重み】誘導撃ち（ターゲットを狙い続ける）を行う確率割合")]
    public float trackingFireWeight = 70f;
    [Tooltip("【重み】ランダム撃ち（砲塔を振り回してばらまく）を行う確率割合")]
    public float randomFireWeight = 30f;

    [Header("--- Boss: Jamming Settings ---")]
    [Tooltip("ジャミング攻撃を解禁するHPの割合（0.5ならHP半分以下で発動）")]
    [Range(0.1f, 1.0f)] public float jammingHpThreshold = 0.5f;
    [Tooltip("ジャミング攻撃を行う基本間隔（秒）")]
    public float jammingBaseInterval = 6.0f;
    [Tooltip("ジャミング間隔の誤差（±この秒数だけランダムにズレる）")]
    public float jammingVariance = 1.5f;
    [Tooltip("ジャミング波の最大到達半径")]
    public float jammingMaxRadius = 15.0f;
    [Tooltip("ジャミング波が広がるスピード")]
    public float jammingExpandSpeed = 15.0f;
    [Tooltip("ジャミングに触れた際に行動不能になる時間（秒）")]
    public float jammingStunDuration = 2.0f;

    // ★追加: 種類ごとに暴走時の「加算する速度」を設定できるようにする（インスペクターで変更可能）
    [Tooltip("ジャミング波に触れた自爆戦車に【加算】する暴走時の移動速度（例: +20）")]
    public float berserkBonusSpeed = 5.0f;

    [Tooltip("ジャミング波のマテリアル（半透明の黄色などを設定。空欄でも自動生成します）")]
    public Material jammingMaterial;

    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private LineRenderer _lineRenderer; // ★追加: デバッグ射線用
    private TankStatus _currentTarget;
    private Vector3 _moveTarget;
    private float _moveTimer;
    private float _stuckTimer;
    private float _nextTargetUpdateTime = 0f;
    private Vector3 _smoothedMoveDir;
    private const float STAGE_LIMIT = 13.5f;
    private float _turretNoiseTime;
    private Quaternion _independentTurretRotation;
    private bool _isActionRigid = false;
    private int _currentAmmoCount;
    private float _fireCooldownTimer = 0f;
    private Vector3 _smartAimDir = Vector3.zero;
    private float _smartAimTimer = 0f;
    private TankStatus _leaderTarget;
    private int _partsDropCount = 0;
    private bool _hasDroppedParts = false;

    private bool _isJammingPhase = false;
    private float _jammingTimer = 0f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
        // ★追加: デバッグ射線用のLineRendererセットアップ
        _lineRenderer = GetComponent<LineRenderer>();
        if (_lineRenderer == null) _lineRenderer = gameObject.AddComponent<LineRenderer>();
        _lineRenderer.enabled = false;
        _lineRenderer.startWidth = 0.1f;
        _lineRenderer.endWidth = 0.1f;

        if (turretTransform != null) _independentTurretRotation = turretTransform.rotation;
        else _independentTurretRotation = transform.rotation;

        if (_agent != null)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }

        _currentAmmoCount = tankStatus.GetTotalMaxAmmo();
        DecideNextMoveTarget();

        if (enemyData != null) _partsDropCount = enemyData.partsDropCount;
    }

    private void Update()
    {
        if (tankStatus.IsDead)
        {
            if (!_hasDroppedParts) { _hasDroppedParts = true; DropParts(); }
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            return;
        }

        if (GameManager.Instance != null && (!GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished()))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            return;
        }

        if (_fireCooldownTimer > 0) _fireCooldownTimer -= Time.deltaTime;

        HandleJammingLogic();

        ThinkTarget();
        ThinkMoveLogic();
        HandleTurretAI();

        if (!_isActionRigid) ThinkMine();

        // ★追加: アンテナ戦車のデバッグ射線を可視化
        if (DebugVisualizer.Instance != null && _lineRenderer != null && firePoint != null)
        {
            int bounces = 0;
            if (tankStatus.GetShellPrefab() != null)
            {
                var shellCtrl = tankStatus.GetShellPrefab().GetComponent<ShellController>();
                if (shellCtrl != null && shellCtrl.shellData != null) bounces = shellCtrl.shellData.maxBounces;
            }
            bounces += tankStatus.bonusBounces;
            DebugVisualizer.Instance.DrawTrajectoryLine(_lineRenderer, firePoint.position, firePoint.forward, bounces);
        }
    }

    private void FixedUpdate()
    {
        if (_rb == null || _rb.isKinematic || tankStatus.IsInStun) return;
        if (GameManager.Instance != null && (!GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished())) return;

        if (_isActionRigid)
        {
            StopMovementImmediate();
            return;
        }

        ExecuteMovement();
    }

    private void LateUpdate()
    {
        if (tankStatus.IsDead || tankStatus.IsInStun) return;
        if (!_isActionRigid) HandleTurretRotation();
    }

    private void HandleJammingLogic()
    {
        if (!_isJammingPhase && tankStatus.CurrentHp <= tankStatus.GetData().maxHp * jammingHpThreshold)
        {
            _isJammingPhase = true;
            _jammingTimer = jammingBaseInterval + Random.Range(-jammingVariance, jammingVariance);
            if (EffectManager.Instance != null) EffectManager.Instance.PlayExplosion(transform.position);
        }

        if (_isJammingPhase && !tankStatus.IsInStun && !_isActionRigid)
        {
            _jammingTimer -= Time.deltaTime;
            if (_jammingTimer <= 0f)
            {
                StartCoroutine(ExecuteJammingRoutine());
                _jammingTimer = jammingBaseInterval + Random.Range(-jammingVariance, jammingVariance);
            }
        }
    }

    private IEnumerator ExecuteJammingRoutine()
    {
        _isActionRigid = true;

        float chargeTime = 1.0f;
        float timer = 0f;
        Vector3 origPos = transform.position;

        while (timer < chargeTime)
        {
            timer += Time.deltaTime;
            transform.position = origPos + (Vector3)Random.insideUnitCircle * 0.05f;
            yield return null;
        }
        transform.position = origPos;

        GameObject waveObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        waveObj.transform.position = transform.position;
        waveObj.transform.localScale = Vector3.zero;

        Collider col = waveObj.GetComponent<Collider>();
        col.isTrigger = true;

        Rigidbody rb = waveObj.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        MeshRenderer renderer = waveObj.GetComponent<MeshRenderer>();
        if (jammingMaterial != null) renderer.material = jammingMaterial;
        else
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 1f, 0f, 0.4f);
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;
        }

        JammingWave wave = waveObj.AddComponent<JammingWave>();
        wave.ownerObj = this.gameObject;
        wave.maxRadius = jammingMaxRadius;
        wave.expandSpeed = jammingExpandSpeed;
        wave.stunDuration = jammingStunDuration;

        // ★追加: 暴走時の速度アップ値を波に持たせて伝達する
        wave.berserkBonusSpeed = this.berserkBonusSpeed;

        yield return new WaitForSeconds(0.5f);
        _isActionRigid = false;
    }

    public void TryFire()
    {
        if (tankStatus.IsInStun || _isActionRigid || _fireCooldownTimer > 0 || _currentAmmoCount <= 0) return;

        int wallLayerMask = LayerMask.GetMask("Wall");
        Vector3 muzzlePos = firePoint.position;
        Vector3 turretCenter = turretTransform != null ? turretTransform.position : transform.position;

        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;
        if (Physics.CheckSphere(muzzlePos, checkRadius, wallLayerMask)) return;
        if (Physics.Linecast(turretCenter, muzzlePos, wallLayerMask)) return;

        StartCoroutine(BurstFireRoutine());
    }

    private IEnumerator BurstFireRoutine()
    {
        _isActionRigid = true;

        float roll = Random.Range(0f, trackingFireWeight + randomFireWeight);
        bool isTrackingFire = (roll < trackingFireWeight);
        float randomSpinSpeed = Random.Range(-180f, 180f);

        for (int i = 0; i < burstCount; i++)
        {
            if (tankStatus.IsDead || tankStatus.IsInStun) break;

            _currentAmmoCount--;

            if (tankStatus.GetShellPrefab() != null && firePoint != null)
            {
                if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(firePoint);
                GameObject shellObj = Instantiate(tankStatus.GetShellPrefab(), firePoint.position, firePoint.rotation);
                EffectManager.Instance.ShotSound();

                ShellController shell = shellObj.GetComponent<ShellController>();
                if (shell != null) shell.Launch(gameObject, 0);
            }

            StartCoroutine(ReloadAmmoRoutine());

            float waitTime = (i < burstCount - 1) ? burstInterval : tankStatus.GetData().shotDelay;
            float t = 0;

            while (t < waitTime)
            {
                t += Time.deltaTime;
                if (isTrackingFire)
                {
                    if (_currentTarget != null)
                    {
                        Vector3 dir = (_currentTarget.transform.position - turretTransform.position).normalized;
                        dir.y = 0;
                        if (dir != Vector3.zero)
                        {
                            _independentTurretRotation = Quaternion.RotateTowards(_independentTurretRotation, Quaternion.LookRotation(dir), enemyData.turretRotationSpeed * Time.deltaTime);
                        }
                    }
                }
                else
                {
                    _independentTurretRotation *= Quaternion.Euler(0, randomSpinSpeed * Time.deltaTime, 0);
                }

                if (turretTransform != null) turretTransform.rotation = _independentTurretRotation;
                yield return null;
            }
        }

        _fireCooldownTimer = enemyData.fireCooldown;
        _isActionRigid = false;
    }

    private IEnumerator ReloadAmmoRoutine()
    {
        yield return new WaitForSeconds(tankStatus.GetData().ammoCooldown);
        int totalMax = tankStatus.GetTotalMaxAmmo();
        if (_currentAmmoCount < totalMax) _currentAmmoCount++;
    }

    private void ThinkMoveLogic()
    {
        _moveTimer += Time.deltaTime;
        bool shouldUpdateTarget = false;

        if (enemyData.aiType != EnemyData.AIType.Neat)
        {
            float distToDest = (_agent != null && _agent.isOnNavMesh && _agent.hasPath) ? _agent.remainingDistance : Vector3.Distance(transform.position, _moveTarget);
            if (distToDest < 2.0f || _moveTimer > 5.0f) shouldUpdateTarget = true;
        }

        if (shouldUpdateTarget) { DecideNextMoveTarget(); _moveTimer = 0f; }

        if (_rb.linearVelocity.magnitude < 0.1f && !_isActionRigid && enemyData.aiType != EnemyData.AIType.Neat)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer > 1.0f) { DecideNextMoveTarget(); _stuckTimer = 0f; }
        }
        else _stuckTimer = 0f;

        if (_agent != null && _agent.isOnNavMesh)
        {
            Vector3 finalDestination = _moveTarget;
            switch (enemyData.aiType)
            {
                case EnemyData.AIType.Neat: finalDestination = transform.position; break;
                case EnemyData.AIType.Idiot:
                case EnemyData.AIType.Wanderer: finalDestination = _moveTarget; break;
                case EnemyData.AIType.Aggressive:
                    if (_currentTarget != null)
                    {
                        float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);
                        if (dist > 5.0f) finalDestination = _currentTarget.transform.position;
                        else if (dist < 3.0f) finalDestination = transform.position + (transform.position - _currentTarget.transform.position).normalized * 3.0f;
                        else finalDestination = transform.position + (Vector3.Cross((_currentTarget.transform.position - transform.position).normalized, Vector3.up).normalized) * 3.0f;
                    }
                    break;
                case EnemyData.AIType.Coward:
                    if (_currentTarget != null)
                    {
                        if (Vector3.Distance(transform.position, _currentTarget.transform.position) < 10.0f)
                        {
                            Vector3 awayDir = (transform.position - _currentTarget.transform.position).normalized;
                            Vector3 runDir = (awayDir + new Vector3(Mathf.Sin(Time.time * 2f), 0, Mathf.Cos(Time.time * 2f)) * 0.5f).normalized;
                            Vector3 targetPos = transform.position + runDir * 6.0f;
                            if (NavMesh.SamplePosition(targetPos, out NavMeshHit navHit, 4.0f, NavMesh.AllAreas)) finalDestination = navHit.position;
                            else finalDestination = transform.position + ((Vector3.zero - transform.position).normalized) * 3.0f;
                        }
                        else finalDestination = _moveTarget;
                    }
                    break;
                case EnemyData.AIType.Sycophant:
                    if (_leaderTarget == null || _leaderTarget.IsDead)
                    {
                        var allies = FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Where(t => t.team == tankStatus.team && t != tankStatus && !t.IsDead).ToList();
                        if (allies.Count > 0) _leaderTarget = allies[Random.Range(0, allies.Count)];
                    }
                    if (_leaderTarget != null)
                    {
                        if (Vector3.Distance(transform.position, _moveTarget) < 1.0f || Vector3.Distance(_moveTarget, _leaderTarget.transform.position) > 4.0f)
                        {
                            Vector3 targetPos = _leaderTarget.transform.position + new Vector3((Random.insideUnitCircle * 3.0f).x, 0, (Random.insideUnitCircle * 3.0f).y);
                            if (NavMesh.SamplePosition(targetPos, out NavMeshHit navHit, 3.0f, NavMesh.AllAreas)) _moveTarget = navHit.position;
                            else _moveTarget = _leaderTarget.transform.position;
                        }
                        finalDestination = _moveTarget;
                    }
                    break;
                case EnemyData.AIType.Leadership:
                    if (CountAllies() > 0) { if (_currentTarget != null) finalDestination = transform.position + (transform.position - _currentTarget.transform.position).normalized * 5.0f; }
                    else { if (_currentTarget != null) finalDestination = _currentTarget.transform.position; }
                    break;
            }
            _agent.SetDestination(finalDestination);
        }
    }

    private void DecideNextMoveTarget()
    {
        _moveTimer = 0;
        switch (enemyData.aiType)
        {
            case EnemyData.AIType.Neat: _moveTarget = transform.position; break;
            case EnemyData.AIType.Idiot: _moveTarget = GetRandomStagePoint(); break;
            case EnemyData.AIType.Wanderer:
            case EnemyData.AIType.Sycophant: _moveTarget = GetFarRandomPoint(); break;
            default: _moveTarget = GetRandomStagePoint(); break;
        }
    }

    private void StopMovementImmediate()
    {
        _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
    }

    private void ExecuteMovement()
    {
        if (_isActionRigid || tankStatus.IsInStun || _agent == null || !_agent.isOnNavMesh) { StopMovementImmediate(); return; }

        Vector3 baseDir = _agent.desiredVelocity;
        if (enemyData.aiType != EnemyData.AIType.Neat && baseDir.magnitude < 0.1f) baseDir = transform.forward * tankStatus.GetCurrentMoveSpeed();
        if (enemyData.aiType != EnemyData.AIType.Neat && baseDir.magnitude > 0.1f)
        {
            float wobbleSpeed = (enemyData.aiType == EnemyData.AIType.Idiot) ? 3f : 1.5f;
            float wobbleAmount = (enemyData.aiType == EnemyData.AIType.Idiot) ? 0.8f : 0.3f;
            baseDir += Vector3.Cross(baseDir.normalized, Vector3.up) * Mathf.Sin(Time.time * wobbleSpeed) * wobbleAmount;
        }

        Vector3 finalDir = baseDir.normalized;

        Vector3 tankAvoid = GetAvoidanceVector("Tank");
        if (tankAvoid != Vector3.zero) finalDir = (finalDir + tankAvoid * 5.0f).normalized;

        Vector3 deadlyAvoid = GetAvoidanceVector("Deadly");
        if (deadlyAvoid != Vector3.zero) finalDir = (finalDir * 0.4f + deadlyAvoid * 3.0f).normalized;

        Vector3 wallAvoid = GetWallAvoidanceVector(5.0f);
        if (wallAvoid != Vector3.zero) finalDir = (finalDir * 0.2f + wallAvoid * 5.0f).normalized;

        if (finalDir.magnitude < 0.1f) finalDir = transform.forward;

        int obstacleMask = LayerMask.GetMask("Wall", "Spike");
        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.5f, finalDir, out RaycastHit sphereHit, 1.2f, obstacleMask))
        {
            Vector3 wallNormal = sphereHit.normal; wallNormal.y = 0;
            Vector3 slideDir = Vector3.ProjectOnPlane(finalDir, wallNormal);
            finalDir = (slideDir.magnitude > 0.1f) ? slideDir.normalized : wallNormal.normalized;
        }

        if (finalDir != Vector3.zero) _smoothedMoveDir = Vector3.Lerp(_smoothedMoveDir, finalDir, Time.fixedDeltaTime * 6.0f).normalized;
        if (_smoothedMoveDir == Vector3.zero) _smoothedMoveDir = transform.forward;

        if (_smoothedMoveDir.magnitude > 0.1f && enemyData.aiType != EnemyData.AIType.Neat)
        {
            float targetAngle = Mathf.Atan2(_smoothedMoveDir.x, _smoothedMoveDir.z) * Mathf.Rad2Deg;
            float currentY = _rb.rotation.eulerAngles.y;
            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            if (Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle)) > 45.0f) _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            else
            {
                Vector3 vel = transform.forward * tankStatus.GetCurrentMoveSpeed();
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
        }
        else StopMovementImmediate();

        if (_agent != null && _agent.isOnNavMesh) _agent.nextPosition = _rb.position;
    }

    private Vector3 GetAvoidanceVector(string type)
    {
        float maxSearchRadius = 3.5f;
        if (enemyData != null) maxSearchRadius = Mathf.Max(maxSearchRadius, enemyData.shellAvoidRadius, enemyData.mineAvoidRadius, enemyData.allyMineAvoidRadius);

        Collider[] hits = Physics.OverlapSphere(transform.position, maxSearchRadius);
        Vector3 avoidVec = Vector3.zero;

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject || hit.transform.IsChildOf(transform)) continue;
            Vector3 toObj = hit.transform.position - transform.position;
            float dist = toObj.magnitude;
            if (dist == 0) continue;

            Vector3 awayDir = -toObj.normalized; awayDir.y = 0;

            if (type == "Deadly")
            {
                if (hit.CompareTag("Shell"))
                {
                    float avoidRad = (enemyData != null) ? enemyData.shellAvoidRadius : 3.0f;
                    if (dist < avoidRad) avoidVec += awayDir * (1.0f - dist / avoidRad);
                }
                else if (hit.CompareTag("Mine"))
                {
                    TeamType mineTeam = TeamType.Neutral;
                    var mineCtrl = hit.GetComponent<MineController>();
                    if (mineCtrl != null) mineTeam = mineCtrl.GetTeam();
                    else { var robot = hit.GetComponent<RobotBombController>(); if (robot != null) mineTeam = robot.GetTeam(); }

                    float avoidRad = 3.0f;
                    if (enemyData != null) avoidRad = (mineTeam == tankStatus.team) ? enemyData.allyMineAvoidRadius : enemyData.mineAvoidRadius;
                    if (dist < avoidRad) avoidVec += awayDir * (1.0f - dist / avoidRad);
                }
            }
            else if (type == "Tank")
            {
                TankStatus otherTank = hit.GetComponentInParent<TankStatus>();
                if (otherTank != null && !otherTank.IsDead)
                {
                    if (dist < 3.5f) avoidVec += awayDir * (1.0f - dist / 3.5f);
                }
            }
        }
        return avoidVec;
    }

    private Vector3 GetWallAvoidanceVector(float maxDist)
    {
        Vector3 avoidVec = Vector3.zero;
        int obstacleMask = LayerMask.GetMask("Wall", "Spike");
        float[] angles = { 0, 30, -30, 60, -60, 90, -90 };

        foreach (float angle in angles)
        {
            Vector3 dir = Quaternion.Euler(0, angle, 0) * transform.forward;
            float checkDist = (Mathf.Abs(angle) >= 90) ? maxDist * 0.6f : maxDist;
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, dir, out RaycastHit hit, checkDist, obstacleMask))
            {
                avoidVec += hit.normal * (1.0f - (hit.distance / checkDist));
            }
        }
        return avoidVec;
    }

    private void ThinkMine()
    {
        if (!enemyData.useMine || tankStatus.ActiveMineCount >= tankStatus.GetTotalMineLimit()) return;

        // 近くに既存の地雷やスポナーがあれば置かない（密集防止）
        if (Physics.OverlapSphere(transform.position, enemyData.minePlacementSpacing).Any(c => c.CompareTag("Mine"))) return;

        bool shouldPlace = false;
        float distToTarget = (_currentTarget != null) ? Vector3.Distance(transform.position, _currentTarget.transform.position) : 999f;

        switch (enemyData.aiType)
        {
            case EnemyData.AIType.Idiot: if (CountAlliesNearby(5.0f) == 0 && Random.value < 0.01f) shouldPlace = true; break;
            case EnemyData.AIType.Coward: if (distToTarget < 6.0f) shouldPlace = true; break;
            case EnemyData.AIType.Aggressive: if (distToTarget < 5.0f) shouldPlace = true; break;
            case EnemyData.AIType.Wanderer: if (Random.value < 0.02f) shouldPlace = true; break;
            case EnemyData.AIType.Sycophant: if (_leaderTarget == null && Random.value < 0.02f) shouldPlace = true; break;
            case EnemyData.AIType.Leadership: if ((CountAllies() > 0 && distToTarget < 6.0f) || (CountAllies() == 0 && distToTarget < 5.0f)) shouldPlace = true; break;
        }

        if (shouldPlace) StartCoroutine(MineRoutine());
    }

    private IEnumerator MineRoutine()
    {
        _isActionRigid = true;
        GameObject prefabToUse = minePrefab != null ? minePrefab : tankStatus.GetMinePrefab();

        if (prefabToUse != null)
        {
            GameObject mineObj = Instantiate(prefabToUse, transform.position, Quaternion.identity);
            if (mineObj.TryGetComponent(out MineController mineCtrl))
            {
                mineCtrl.Init(tankStatus, tankStatus.GetMineData());
                tankStatus.OnMinePlaced();
            }
            else if (mineObj.TryGetComponent(out RobotBombController robotBomb))
            {
                robotBomb.Init(tankStatus, tankStatus.GetMineData());
                tankStatus.OnMinePlaced();
            }
            else if (mineObj.TryGetComponent(out TankSpawnerBox spawnerBox))
            {
                spawnerBox.Init(tankStatus, tankStatus.team);
                tankStatus.OnMinePlaced();
            }
        }

        // ★修正1: スポーンボックスや地雷を置いたときの硬直時間を、通常の弾(shotDelay)より短くする
        // ボスは設置後すぐに動けるように 0.3秒 に固定
        yield return new WaitForSeconds(0.3f);
        _isActionRigid = false;

        // ★修正2: 設置した直後、自分が置いたスポナーに引っかからないように、
        // 直ちに「現在地から離れた安全なランダム地点」を次の目的地として強制的に上書きする
        if (_agent != null && _agent.isOnNavMesh)
        {
            Vector3 awayDir = (transform.position - _moveTarget).normalized;
            if (awayDir == Vector3.zero) awayDir = transform.forward;

            // 設置場所から 4m 以上離れた場所を探す
            Vector3 escapeTarget = transform.position + awayDir * 5.0f + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));

            if (NavMesh.SamplePosition(escapeTarget, out NavMeshHit hit, 4.0f, NavMesh.AllAreas))
            {
                _moveTarget = hit.position;
                _agent.SetDestination(_moveTarget);
                _moveTimer = 0f; // 移動タイマーをリセットして即座に向かわせる
            }
        }
    }

    private void HandleTurretAI()
    {
        if (turretTransform == null) return;

        Vector3 targetDir = Vector3.forward;
        if (_currentTarget != null)
        {
            targetDir = (_currentTarget.transform.position - turretTransform.position).normalized;
            if (enemyData.useSmartRicochet)
            {
                _smartAimTimer -= Time.deltaTime;
                if (_smartAimTimer <= 0f) { _smartAimDir = FindSmartRicochetDirection(); _smartAimTimer = 0.1f; }
                if (_smartAimDir != Vector3.zero) targetDir = _smartAimDir;
            }
        }
        else targetDir = transform.forward;
        targetDir.y = 0;

        bool canShoot = CheckShootTrajectory();
        float offsetAngle = 0f;

        if (!canShoot && _smartAimDir == Vector3.zero)
        {
            _turretNoiseTime += Time.deltaTime * 0.8f;
            offsetAngle = (Mathf.PerlinNoise(_turretNoiseTime, 0f) * 2.0f - 1.0f) * (enemyData.turretSearchAngle + 30f);
        }

        if (targetDir != Vector3.zero)
        {
            _independentTurretRotation = Quaternion.RotateTowards(_independentTurretRotation, Quaternion.LookRotation(targetDir) * Quaternion.Euler(0, offsetAngle, 0), enemyData.turretRotationSpeed * Time.deltaTime);
        }
    }

    private void HandleTurretRotation()
    {
        if (turretTransform != null) turretTransform.rotation = _independentTurretRotation;
        if (CheckShootTrajectory()) TryFire();
    }

    private void ThinkTarget()
    {
        if (_currentTarget != null && !_currentTarget.IsDead)
        {
            if (enemyData.targetStrategy == EnemyData.TargetStrategy.Persistent) return;
            else if (enemyData.targetStrategy == EnemyData.TargetStrategy.Capricious && Time.time < _nextTargetUpdateTime) return;
        }

        _currentTarget = FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Where(t => t.team != tankStatus.team && !t.IsDead).OrderBy(t => Vector3.Distance(transform.position, t.transform.position)).FirstOrDefault();
        if (enemyData.targetStrategy == EnemyData.TargetStrategy.Capricious) _nextTargetUpdateTime = Time.time + 3.0f + Random.Range(0f, 3.0f);
    }

    private bool CheckShootTrajectory()
    {
        if (_currentTarget == null || firePoint == null) return false;

        Vector3 startPos = firePoint.position;
        Vector3 dir = firePoint.forward;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        Collider[] closeHits = Physics.OverlapSphere(startPos, 2.0f);
        foreach (var hit in closeHits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (hit.CompareTag("Mine")) return false;
            if (enemyData != null && enemyData.isTeamAware)
            {
                TankStatus closeTank = hit.GetComponentInParent<TankStatus>();
                if (closeTank != null && closeTank.team == tankStatus.team && !closeTank.IsDead) return false;
            }
        }

        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (enemyData != null && !enemyData.considerReflection) maxBounces = 0;

        if (enemyData != null && enemyData.useSmartRicochet && _smartAimDir != Vector3.zero)
        {
            if (Vector3.Angle(dir, _smartAimDir) <= enemyData.shotAllowAngle)
            {
                if (SimulateRaycastTrajectory(startPos, _smartAimDir, maxBounces, layerMask, 0))
                {
                    if (turretTransform != null) { turretTransform.rotation = Quaternion.LookRotation(_smartAimDir); _independentTurretRotation = turretTransform.rotation; }
                    return true;
                }
                else { _smartAimDir = Vector3.zero; _smartAimTimer = 0f; return false; }
            }
            return false;
        }
        return SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0);
    }

    private Vector3 GetRandomStagePoint() => new Vector3(Random.Range(-STAGE_LIMIT, STAGE_LIMIT), 0, Random.Range(-STAGE_LIMIT, STAGE_LIMIT));
    private Vector3 GetFarRandomPoint() { for (int i = 0; i < 10; i++) { Vector3 p = GetRandomStagePoint(); if (Vector3.Distance(transform.position, p) > 10.0f) return p; } return -transform.position; }
    private int CountAllies() => FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Count(t => t.team == tankStatus.team && t != tankStatus && !t.IsDead);
    private int CountAlliesNearby(float radius) => Physics.OverlapSphere(transform.position, radius).Select(c => c.GetComponentInParent<TankStatus>()).Count(t => t != null && t.team == tankStatus.team && t != tankStatus && !t.IsDead);

    private Vector3 FindSmartRicochetDirection()
    {
        if (firePoint == null || _currentTarget == null) return Vector3.zero;
        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (maxBounces <= 0 || enemyData == null || !enemyData.considerReflection) return Vector3.zero;

        Vector3 startPos = firePoint.position;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike") & ~LayerMask.GetMask("Mine") & ~LayerMask.GetMask("Ignore Raycast");

        Vector3 baseDir = (_currentTarget.transform.position - startPos).normalized;
        baseDir.y = 0;
        if (baseDir == Vector3.zero) baseDir = transform.forward;

        for (int angle = 0; angle <= 180; angle += 3)
        {
            Vector3 rightDir = Quaternion.Euler(0, angle, 0) * baseDir;
            if (SimulateRaycastTrajectory(startPos, rightDir, maxBounces, layerMask, 0)) return rightDir;
            if (angle != 0 && angle != 180)
            {
                Vector3 leftDir = Quaternion.Euler(0, -angle, 0) * baseDir;
                if (SimulateRaycastTrajectory(startPos, leftDir, maxBounces, layerMask, 0)) return leftDir;
            }
        }
        return Vector3.zero;
    }

    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce)
    {
        if (currentBounce > 15) return false;
        dir.y = 0; dir.Normalize();

        RaycastHit[] hits = Physics.SphereCastAll(startPos, (enemyData != null) ? enemyData.raycastRadius : 0.25f, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance == 0) continue;

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                if (bouncesLeft > 0)
                {
                    Vector3 reflectDir = Vector3.Reflect(dir, hit.normal);
                    reflectDir.y = 0; reflectDir.Normalize();
                    return SimulateRaycastTrajectory(hit.point + hit.normal * 0.05f, reflectDir, bouncesLeft - 1, layerMask, currentBounce + 1);
                }
                return false;
            }

            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null) return hitTank.team != tankStatus.team;
        }
        return false;
    }

    private void DropParts()
    {
        if (_partsDropCount <= 0 || GameManager.Instance == null) return;
        GameObject prefab = GameManager.Instance.GetPartsItemPrefab();
        if (prefab == null) return;

        var survivingPlayers = FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Where(t => t.team == TeamType.Blue && !t.IsDead).ToList();
        TankStatus targetPlayer = tankStatus.LastAttacker;

        bool isFriendlyFire = (targetPlayer != null && targetPlayer.team != TeamType.Blue);
        bool isBoss = (enemyData != null && enemyData.isBossDrop);

        if (isFriendlyFire) SpawnAndMagnetParts(prefab, _partsDropCount, null);
        else if (isBoss)
        {
            int survivingCount = survivingPlayers.Count;
            if (survivingCount == 0) return;
            int partsPerPlayer = Mathf.Max(0, _partsDropCount + 1 - survivingCount);
            foreach (var player in survivingPlayers) SpawnAndMagnetParts(prefab, partsPerPlayer, player);
        }
        else
        {
            if (targetPlayer == null || targetPlayer.IsDead || targetPlayer.team != TeamType.Blue) targetPlayer = survivingPlayers.OrderBy(t => Vector3.Distance(transform.position, t.transform.position)).FirstOrDefault();
            SpawnAndMagnetParts(prefab, _partsDropCount, targetPlayer);
        }
    }

    private void SpawnAndMagnetParts(GameObject prefab, int count, TankStatus targetPlayer)
    {
        for (int i = 0; i < count; i++)
        {
            GameObject partObj = Instantiate(prefab, transform.position + Vector3.up * 1.0f, Quaternion.identity);
            Rigidbody rb = partObj.GetComponent<Rigidbody>();
            if (rb == null) rb = partObj.AddComponent<Rigidbody>();
            rb.AddForce(Vector3.up * 2.5f + Random.insideUnitSphere * 1.5f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 50f, ForceMode.Impulse);

            if (targetPlayer != null)
            {
                PartsItemController pic = partObj.GetComponent<PartsItemController>();
                if (pic != null) pic.StartMagneticEffect(targetPlayer);
            }
        }
    }
}

// ============================================
// ★ジャミング波の当たり判定と拡大用コンポーネント
// ============================================
public class JammingWave : MonoBehaviour
{
    public float maxRadius = 15f;
    public float expandSpeed = 15f;
    public float stunDuration = 2f;
    public float berserkBonusSpeed = 5.0f; // ★追加: 暴走速度アップ値を受け取る用
    public GameObject ownerObj;

    private void Update()
    {
        transform.localScale += Vector3.one * expandSpeed * Time.deltaTime;
        if (transform.localScale.x >= maxRadius * 2f) Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        MineController mine = other.GetComponentInParent<MineController>();
        if (mine != null) { mine.Explode(); return; }

        RobotBombController robot = other.GetComponentInParent<RobotBombController>();
        if (robot != null) { robot.Explode(); return; }

        TankStatus tank = other.GetComponentInParent<TankStatus>();
        if (tank != null && tank.gameObject != ownerObj)
        {
            if (tank.GetData() != null && tank.GetData().isSelfDestruct)
            {
                // ★修正: 波から受け取った「速度アップ値」を戦車に渡す
                tank.ActivateJammingBerserk(berserkBonusSpeed);
            }
            else
            {
                tank.ApplyJamming(stunDuration);
            }
        }
    }
}