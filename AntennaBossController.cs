using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class AntennaBossController : MonoBehaviour
{
    [Header("Settings")]
    public EnemyData enemyData;
    public TankStatus tankStatus;
    public Transform turretTransform;
    public Transform firePoint;
    public GameObject minePrefab;

    [Header("Debug Settings")]
    public bool isDebugMode = false;

    [Header("--- Boss: Burst Fire Settings ---")]
    public int burstCount = 3;
    public float burstInterval = 0.15f;
    public float trackingFireWeight = 70f;
    public float randomFireWeight = 30f;

    [Header("--- Boss: Jamming Settings ---")]
    [Range(0.1f, 1.0f)] public float jammingHpThreshold = 0.5f;
    public float jammingBaseInterval = 6.0f;
    public float jammingVariance = 1.5f;
    public float jammingMaxRadius = 15.0f;
    public float jammingExpandSpeed = 15.0f;
    public float jammingStunDuration = 2.0f;
    public float berserkBonusSpeed = 5.0f;
    public Material jammingMaterial;

    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private LineRenderer _lineRenderer;
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
    private bool _hasBattleStarted = false;
    private bool _lastDebugMode = false;
    private Vector2 _debugMoveInput;

    // ==========================================
    // ★追加: 厳格な配置管理とクールタイム
    // ==========================================
    private List<GameObject> _activeSpawners = new List<GameObject>();
    private List<TankStatus> _spawnedTanks = new List<TankStatus>();
    private float _mineCooldownTimer = 0f; // 絶対に連発させないためのクールタイム

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
    }

    private void Start()
    {
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
            _agent.enabled = !isDebugMode;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 10.0f, NavMesh.AllAreas))
        {
            float offsetY = 0f;
            Collider[] cols = GetComponentsInChildren<Collider>();
            float minColY = float.MaxValue;
            bool foundCol = false;

            foreach (var c in cols)
            {
                if (!c.isTrigger)
                {
                    if (c.bounds.min.y < minColY) minColY = c.bounds.min.y;
                    foundCol = true;
                }
            }

            if (foundCol) offsetY = transform.position.y - minColY;

            Vector3 groundPos = new Vector3(transform.position.x, navHit.position.y + offsetY + 0.05f, transform.position.z);
            transform.position = groundPos;
            if (_agent != null && _agent.enabled) _agent.Warp(groundPos);
        }

        _currentAmmoCount = tankStatus.GetTotalMaxAmmo();
        DecideNextMoveTarget();

        if (enemyData != null) _partsDropCount = enemyData.partsDropCount;
        _lastDebugMode = isDebugMode;
    }

    private void Update()
    {
        if (tankStatus.IsDead)
        {
            if (!_hasDroppedParts) { _hasDroppedParts = true; DropParts(); }
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            return;
        }

        if (GameManager.Instance != null && (!GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished()))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (!_hasBattleStarted) _rb.isKinematic = true;
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            return;
        }

        if (!_hasBattleStarted)
        {
            _hasBattleStarted = true;
            _rb.isKinematic = false;
            _rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_moveTarget);
            }
        }

        if (_lastDebugMode != isDebugMode)
        {
            if (isDebugMode)
            {
                if (_agent != null && _agent.isOnNavMesh) { _agent.isStopped = true; _agent.enabled = false; }
                _isActionRigid = false;
            }
            else
            {
                if (_agent != null) { _agent.enabled = true; _agent.Warp(transform.position); }
                if (_agent != null && _agent.isOnNavMesh) { _agent.isStopped = false; _agent.SetDestination(_moveTarget); }
            }
            _lastDebugMode = isDebugMode;
        }

        if (_fireCooldownTimer > 0) _fireCooldownTimer -= Time.deltaTime;

        // ★追加: 厳密な設置クールタイムの進行
        if (_mineCooldownTimer > 0) _mineCooldownTimer -= Time.deltaTime;

        if (isDebugMode)
        {
            HandleDebugInput();
        }
        else
        {
            HandleJammingLogic();
            ThinkTarget();
            ThinkMoveLogic();
            HandleTurretAI();

            if (!_isActionRigid) ThinkMine();
        }

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

    private void HandleDebugInput()
    {
        if (Keyboard.current == null) return;
        float h = 0f; float v = 0f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h = -1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h = 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v = 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v = -1f;
        _debugMoveInput = new Vector2(h, v);

        if (Keyboard.current.spaceKey.wasPressedThisFrame && !_isActionRigid) StartCoroutine(ExecuteJammingRoutine());

        bool firePressed = (Mouse.current != null && Mouse.current.leftButton.isPressed) || Keyboard.current.zKey.isPressed;
        if (firePressed && !_isActionRigid && _fireCooldownTimer <= 0)
        {
            _currentAmmoCount = tankStatus.GetData().maxAmmo;
            StartCoroutine(BurstFireRoutine());
        }

        // ★追加: マウスのホイールクリック（中ボタン）で地雷 / タンクスポーンボックスを設置
        if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame && !_isActionRigid)
        {
            StartCoroutine(MineRoutine());
        }

        if (turretTransform != null && Mouse.current != null)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
            {
                Vector3 hitPoint = ray.GetPoint(enter);
                Vector3 dir = (hitPoint - turretTransform.position).normalized;
                dir.y = 0;
                if (dir != Vector3.zero) _independentTurretRotation = Quaternion.LookRotation(dir);
            }
        }
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
                if (isTrackingFire && !isDebugMode)
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
                else if (!isDebugMode)
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

        float distToDest = (_agent != null && _agent.isOnNavMesh && _agent.hasPath) ? _agent.remainingDistance : Vector3.Distance(transform.position, _moveTarget);
        if (distToDest < 3.0f || _moveTimer > 8.0f)
        {
            DecideNextMoveTarget();
            if (_agent != null && _agent.isOnNavMesh) _agent.SetDestination(_moveTarget);
            _moveTimer = 0f;
        }

        if (_rb.linearVelocity.magnitude < 0.1f && !_isActionRigid && enemyData.aiType != EnemyData.AIType.Neat)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer > 1.0f)
            {
                DecideNextMoveTarget();
                if (_agent != null && _agent.isOnNavMesh) _agent.SetDestination(_moveTarget);
                _stuckTimer = 0f;
            }
        }
        else _stuckTimer = 0f;
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
        if (isDebugMode)
        {
            Vector3 dir = new Vector3(_debugMoveInput.x, 0, _debugMoveInput.y).normalized;
            if (dir.magnitude > 0.1f)
            {
                float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float currentY = _rb.rotation.eulerAngles.y;
                float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime);
                _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

                Vector3 vel = transform.forward * tankStatus.GetCurrentMoveSpeed();
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
            else StopMovementImmediate();
            return;
        }

        if (_isActionRigid || tankStatus.IsInStun || _agent == null || !_agent.isOnNavMesh) { StopMovementImmediate(); return; }

        Vector3 desiredVel = _agent.desiredVelocity;

        if (desiredVel.magnitude < 0.1f)
        {
            desiredVel = transform.forward * tankStatus.GetCurrentMoveSpeed();
        }

        Vector3 dangerDir = GetAvoidanceVector("Deadly");
        if (dangerDir != Vector3.zero)
        {
            Vector3 baseEscape = (desiredVel.magnitude > 0.1f ? desiredVel.normalized : transform.forward);
            desiredVel = Vector3.Lerp(baseEscape, dangerDir.normalized, 0.8f).normalized * tankStatus.GetCurrentMoveSpeed();
        }

        int obstacleMask = LayerMask.GetMask("Wall", "Spike");
        Vector3 avoidanceForce = Vector3.zero;
        Vector3[] rayDirs = { transform.forward, Quaternion.Euler(0, 35, 0) * transform.forward, Quaternion.Euler(0, -35, 0) * transform.forward };
        foreach (var rDir in rayDirs)
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, rDir, out RaycastHit rayHit, 3.5f, obstacleMask))
            {
                float strength = 1.0f - (rayHit.distance / 3.5f);
                avoidanceForce += rayHit.normal * strength;
            }
        }
        if (avoidanceForce != Vector3.zero)
        {
            avoidanceForce.y = 0;
            desiredVel = (desiredVel.normalized + avoidanceForce.normalized * 2.0f).normalized * tankStatus.GetCurrentMoveSpeed();
        }

        Vector3 checkDir = desiredVel.magnitude > 0.1f ? desiredVel.normalized : transform.forward;
        float bossRadius = 0.6f;
        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, bossRadius, checkDir, out RaycastHit sphereHit, 1.5f, obstacleMask))
        {
            Vector3 wallNormal = sphereHit.normal; wallNormal.y = 0;
            Vector3 slideVel = Vector3.ProjectOnPlane(desiredVel, wallNormal);
            if (slideVel.magnitude < 0.1f) desiredVel = wallNormal * tankStatus.GetCurrentMoveSpeed();
            else desiredVel = (slideVel.normalized * tankStatus.GetCurrentMoveSpeed()) + (wallNormal * 1.5f);
        }

        if (desiredVel.magnitude > 0.1f && enemyData.aiType != EnemyData.AIType.Neat)
        {
            float targetAngle = Mathf.Atan2(desiredVel.x, desiredVel.z) * Mathf.Rad2Deg;
            float currentY = _rb.rotation.eulerAngles.y;
            float rotSpeed = tankStatus.GetCurrentRotationSpeed();

            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, rotSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(nextAngle, targetAngle));

            if (angleDiff <= 60.0f)
            {
                float speed = tankStatus.GetCurrentMoveSpeed();
                if (angleDiff > 45.0f) speed *= 0.8f;
                Vector3 vel = transform.forward * speed;
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
            else
            {
                _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            }
        }
        else StopMovementImmediate();

        if (_agent.isOnNavMesh)
        {
            Vector3 syncPos = _rb.position;
            syncPos.y = _agent.nextPosition.y;
            _agent.nextPosition = syncPos;
        }
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
        }
        return avoidVec;
    }

    // ==========================================
    // ★修正: クールタイムと配置数の厳密な管理
    // ==========================================
    private void ThinkMine()
    {
        if (tankStatus.isDevilMineLeaker) return;
        if (!enemyData.useMine) return;

        // ★ク��ルタイム中は絶対に置かない
        if (_mineCooldownTimer > 0f) return;

        // 死んだものや壊れたものをリストから掃除
        _activeSpawners.RemoveAll(s => s == null);
        _spawnedTanks.RemoveAll(t => t == null || t.IsDead);

        // 自分が置いたものだけをカウントする
        int activeCount = _activeSpawners.Count + _spawnedTanks.Count;

        int maxLimit = tankStatus.GetData().maxMines;
        if (activeCount >= maxLimit) return; // 上限に達していたら置かない

        if (Physics.OverlapSphere(transform.position, enemyData.minePlacementSpacing).Any(c => c.CompareTag("Mine"))) return;

        bool shouldPlace = false;
        float distToTarget = (_currentTarget != null) ? Vector3.Distance(transform.position, _currentTarget.transform.position) : 999f;

        switch (enemyData.aiType)
        {
            case EnemyData.AIType.Idiot: if (Random.value < 0.01f) shouldPlace = true; break;
            case EnemyData.AIType.Coward: if (distToTarget < 6.0f) shouldPlace = true; break;
            case EnemyData.AIType.Aggressive: if (distToTarget < 5.0f) shouldPlace = true; break;
            case EnemyData.AIType.Wanderer: if (Random.value < 0.02f) shouldPlace = true; break;
            default: if (Random.value < 0.02f) shouldPlace = true; break;
        }

        if (shouldPlace)
        {
            // ★修正: コルーチン（配置モーション）を呼ぶ「前」にクールタイムを発生させ、1フレームの隙に連続で置くのを完全に防ぐ！
            _mineCooldownTimer = 5.0f;
            StartCoroutine(MineRoutine());
        }
    }

    private IEnumerator MineRoutine()
    {
        _isActionRigid = true; // ★モーション硬直

        GameObject prefabToUse = minePrefab != null ? minePrefab : tankStatus.GetMinePrefab();

        if (prefabToUse != null)
        {
            GameObject mineObj = Instantiate(prefabToUse, transform.position, Quaternion.identity);

            // ★修正: 生成したものはコンポーネントの種類に関わらず「自分が置いたもの」として無条件でリストに追加し、絶対にごまかさせない！
            _activeSpawners.Add(mineObj);

            var mineCtrl = mineObj.GetComponentInChildren<MineController>();
            var robotBomb = mineObj.GetComponentInChildren<RobotBombController>();
            var spawnerBox = mineObj.GetComponentInChildren<TankSpawnerBox>();

            if (mineCtrl != null)
            {
                mineCtrl.Init(tankStatus, tankStatus.GetMineData());
            }
            else if (robotBomb != null)
            {
                robotBomb.Init(tankStatus, tankStatus.GetMineData());
            }
            else if (spawnerBox != null)
            {
                spawnerBox.Init(tankStatus, tankStatus.team);

                // 生まれた戦車を確実に監視下に置く
                spawnerBox.OnTankSpawned += (spawnedTank) =>
                {
                    if (spawnedTank != null) _spawnedTanks.Add(spawnedTank);
                };
            }
        }

        // ★硬直時間（確実に動きを止める）
        yield return new WaitForSeconds(0.5f);
        _isActionRigid = false;

        if (_agent != null && _agent.isOnNavMesh)
        {
            Vector3 awayDir = (transform.position - _moveTarget).normalized;
            if (awayDir == Vector3.zero) awayDir = transform.forward;
            Vector3 escapeTarget = transform.position + awayDir * 5.0f + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));

            if (NavMesh.SamplePosition(escapeTarget, out NavMeshHit hit, 4.0f, NavMesh.AllAreas))
            {
                _moveTarget = hit.position;
                _agent.SetDestination(_moveTarget);
                _moveTimer = 0f;
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
        if (!isDebugMode && CheckShootTrajectory()) TryFire();
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

    private Vector3 FindSmartRicochetDirection()
    {
        if (firePoint == null || _currentTarget == null) return Vector3.zero;
        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (maxBounces <= 0 || enemyData == null || !enemyData.considerReflection) return Vector3.zero;

        Vector3 startPos = firePoint.position;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

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

public class JammingWave : MonoBehaviour
{
    public float maxRadius = 15f;
    public float expandSpeed = 15f;
    public float stunDuration = 2f;
    public float berserkBonusSpeed = 5.0f;
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
            if (tank.GetData() != null && tank.GetData().isSelfDestruct) tank.ActivateJammingBerserk(berserkBonusSpeed);
            else tank.ApplyJamming(stunDuration);
        }
    }
}