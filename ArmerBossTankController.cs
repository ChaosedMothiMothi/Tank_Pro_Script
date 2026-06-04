using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class ArmerBossTankController : MonoBehaviour
{
    [System.Serializable]
    public class WeaponSettings
    {
        public GameObject shellPrefab;
        public Transform[] firePoints;
        public float shotDelay = 2.0f;
        public float burstInterval = 0.15f;
    }

    [Header("基本設定")]
    [SerializeField] private TankStatus tankStatus;
    [SerializeField] private EnemyData enemyData;
    [SerializeField] private Transform turretTransform;
    [SerializeField] private bool isDebugMode = false;

    [Header("Mine Settings")]
    [SerializeField] private GameObject minePrefab;

    [Header("武装設定")]
    [SerializeField] private WeaponSettings mainCannon;
    [SerializeField] private WeaponSettings subCannon;
    private Vector3 _smartAimDir = Vector3.zero;
    private float _smartAimTimer = 0f;

    [Header("ボ���スキル設定")]
    [SerializeField] private float skillBaseCooldown = 5f;
    [SerializeField] private float skillRandomVariance = 2f;
    [SerializeField, Range(0, 1)] private float skillChanceInLine = 0.4f;
    [SerializeField] private bool useSkillOnSubCannonHit = true;

    private const float STAGE_LIMIT_X = 13.5f;
    private const float STAGE_LIMIT_Z = 13.5f;

    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private LineRenderer _lineRenderer;

    private TankStatus _currentTarget;
    private Vector3 _moveTarget;
    private float _moveTimer;
    private float _stuckTimer;
    private bool _lastDebugMode = false;
    private Vector3 _smoothedMoveDir;
    private Vector2 _debugMoveInput;

    private bool _isActionBusy;
    private bool _isActionRigid;
    private int _currentAmmo;
    private float _skillTimer;
    private bool _hasBattleStarted = false;

    private float _fireCooldownTimer = 0f;

    private Quaternion _independentTurretRotation;
    private float _turretNoiseTime;

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
        _lineRenderer.startWidth = 0.15f;
        _lineRenderer.endWidth = 0.15f;

        if (turretTransform != null) _independentTurretRotation = turretTransform.rotation;
        else _independentTurretRotation = transform.rotation;

        if (_agent != null)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
            if (_agent.speed < 1.0f) _agent.speed = 3.5f;

            _agent.enabled = !isDebugMode;
        }

        // ==========================================
        // ★修正: コライダーの底面を自動計算して地面にピッタリ合わせる
        // ==========================================
        if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit navHit, 10.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            float offsetY = 0f;

            // 視界センサー等（isTrigger）を除外した、一番下の物理コライダーの底面を探す
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

            if (foundCol)
            {
                // 「現在の原点��から「コライダーの底面」までの距離をオフセットとする
                offsetY = transform.position.y - minColY;
            }

            // 床の高さ ＋ オフセット ＋ わずかな隙間(0.05f) で絶対にめり込ませない
            Vector3 groundPos = new Vector3(transform.position.x, navHit.position.y + offsetY + 0.05f, transform.position.z);
            transform.position = groundPos;

            if (_agent != null && _agent.enabled) _agent.Warp(groundPos);
        }

        DecideNextMoveTarget();

        if (tankStatus != null && tankStatus.GetData() != null) _currentAmmo = tankStatus.GetData().maxAmmo;
        else _currentAmmo = 1;

        ResetSkillTimer();
        _lastDebugMode = isDebugMode;
    }

    private void Update()
    {
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

        if (tankStatus.IsDead) return;

        if (_lastDebugMode != isDebugMode)
        {
            if (isDebugMode)
            {
                if (_agent != null && _agent.isOnNavMesh) { _agent.isStopped = true; _agent.enabled = false; }
                _rb.isKinematic = false;
                _isActionBusy = false;
                _isActionRigid = false;
            }
            else
            {
                if (_agent != null) { _agent.enabled = true; _agent.Warp(transform.position); }
                if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = false;
                DecideNextMoveTarget();
                if (_agent != null && _agent.isOnNavMesh) _agent.SetDestination(_moveTarget);
            }
            _lastDebugMode = isDebugMode;
        }

        if (_skillTimer > 0) _skillTimer -= Time.deltaTime;

        // ★追加: エネミーデータのクールタイムを適用
        if (_fireCooldownTimer > 0) _fireCooldownTimer -= Time.deltaTime;

        if (isDebugMode) HandleDebugInput();
        else
        {
            ThinkTarget();
            HandleTurretAI();

            // ★修正: 完全硬直中（_isActionRigid）は移動と地雷設置を行わない
            if (!_isActionRigid)
            {
                ThinkMove();
                ThinkMine();
            }
        }

        if (DebugVisualizer.Instance != null && _lineRenderer != null && mainCannon.firePoints != null && mainCannon.firePoints.Length > 0 && mainCannon.firePoints[0] != null)
        {
            int bounces = (mainCannon.shellPrefab?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
            DebugVisualizer.Instance.DrawTrajectoryLine(_lineRenderer, mainCannon.firePoints[0].position, mainCannon.firePoints[0].forward, bounces);
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

    private void HandleDebugInput()
    {
        if (Keyboard.current == null) return;
        float h = 0f; float v = 0f;
        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h = -1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h = 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v = 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v = -1f;
        _debugMoveInput = new Vector2(h, v);

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            _isActionBusy = false; _isActionRigid = false;
            StartCoroutine(FireSkillRoutine());
        }

        bool firePressed = (Mouse.current != null && Mouse.current.leftButton.isPressed) || Keyboard.current.zKey.isPressed;
        if (firePressed)
        {
            if (_currentAmmo <= 0) _currentAmmo = tankStatus.GetData().maxAmmo;
            if (!_isActionBusy) StartCoroutine(FireMainBurstRoutine());
        }

        if (Keyboard.current.mKey.wasPressedThisFrame) StartCoroutine(MineRoutine());

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
                if (dir != Vector3.zero) turretTransform.rotation = Quaternion.LookRotation(dir);
            }
        }
    }

    [Tooltip("AIの思考に基づく物理的な移動処理（滑らかな障害物・地雷のスマート回避）")]
    private void ExecuteMovement()
    {
        // ==========================================
        // ★追加: デバッグモード時の直接移動処理
        // ==========================================
        if (isDebugMode)
        {
            // HandleDebugInput() で取得した _debugMoveInput を使って移動ベクトルを作成
            Vector3 dir = new Vector3(_debugMoveInput.x, 0, _debugMoveInput.y).normalized;

            if (dir.magnitude > 0.1f)
            {
                // 向きたい角度を計算
                float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float currentY = _rb.rotation.eulerAngles.y;

                // 滑らかに旋回
                float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime);
                _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

                // 常に前進（transform.forward）方向にのみ力を加える
                Vector3 vel = transform.forward * tankStatus.GetCurrentMoveSpeed();
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
            else
            {
                // 入力がない時はピタッと止まる
                StopMovementImmediate();
            }

            // デバッグモード中はここで処理を終わらせ、AIの移動処理には進まない
            return;
        }

        if (_isActionRigid || tankStatus.IsInStun || _agent == null || !_agent.isOnNavMesh || !_agent.enabled)
        {
            StopMovementImmediate();
            return;
        }

        Vector3 desiredVel = _agent.desiredVelocity;

        if (desiredVel.magnitude < 0.1f)
        {
            desiredVel = transform.forward * tankStatus.GetCurrentMoveSpeed();
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer > 1.5f)
            {
                DecideNextMoveTarget();
                if (_agent.isOnNavMesh) _agent.SetDestination(_moveTarget);
                _stuckTimer = 0f;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }

        Vector3 dangerDir = CalculateDangerAvoidance();
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

        if (desiredVel.magnitude > 0.1f)
        {
            Vector3 moveDir = desiredVel.normalized;
            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float currentY = _rb.rotation.eulerAngles.y;
            float rotSpeed = tankStatus.GetCurrentRotationSpeed();

            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, rotSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            // ==========================================
            // ★修正: 車体の向きと移動方向を完全に一致させる
            // ==========================================
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(nextAngle, targetAngle));

            if (angleDiff <= 60.0f)
            {
                float speed = tankStatus.GetCurrentMoveSpeed();
                if (angleDiff > 30.0f) speed *= 0.95f; // 少しズレている時は減速して曲がりやすくする

                // カニ歩き（斜め移動）をせず、必ず「車体の正面方向」にのみ進む
                Vector3 vel = transform.forward * speed;
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
            else
            {
                // 向きが大きくズレている時は、その場で旋回に専念する
                _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            }
        }
        else
        {
            StopMovementImmediate();
        }

        // ==========================================
        // ★修正: AIに車体の位置を同期する際、Y座標(高さ)はAIの地面のままにし、無理な引き戻しを防ぐ
        // ==========================================
        if (_agent.isOnNavMesh)
        {
            Vector3 syncPos = _rb.position;
            syncPos.y = _agent.nextPosition.y; // 高さはAIが認識している地面のものを維持
            _agent.nextPosition = syncPos;
        }
    }

    private void ThinkMove()
    {
        _moveTimer += Time.deltaTime;
        if (_agent != null && !_agent.enabled && !isDebugMode) { _agent.enabled = true; _agent.Warp(transform.position); }

        float distToDest = (_agent != null && _agent.isOnNavMesh && _agent.hasPath) ? _agent.remainingDistance : Vector3.Distance(transform.position, _moveTarget);

        // ★修正: 目的地が変わった時だけSetDestinationを呼ぶ（毎フレーム呼ぶとAIがフリーズするため）
        if (distToDest < 3.0f || _moveTimer > 8.0f)
        {
            DecideNextMoveTarget();
            if (_agent != null && _agent.isOnNavMesh) _agent.SetDestination(_moveTarget);
            _moveTimer = 0f;
        }

        if (_rb.linearVelocity.magnitude < 0.1f && !_isActionRigid)
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
        _moveTarget = new Vector3(Random.Range(-STAGE_LIMIT_X, STAGE_LIMIT_X), 0, Random.Range(-STAGE_LIMIT_Z, STAGE_LIMIT_Z));
    }

    private void StopMovementImmediate()
    {
        Vector3 vel = _rb.linearVelocity;
        Vector3 horizontalVel = new Vector3(vel.x, 0, vel.z);
        _rb.AddForce(-horizontalVel * 5.0f, ForceMode.Acceleration);
    }

    private Vector3 CalculateDangerAvoidance()
    {
        float maxRadius = enemyData != null ? Mathf.Max(enemyData.shellAvoidRadius, enemyData.mineAvoidRadius, enemyData.allyMineAvoidRadius) + 2.0f : 5.0f;
        Collider[] hits = Physics.OverlapSphere(transform.position, maxRadius);
        Vector3 totalAvoidVec = Vector3.zero;
        int dangerCount = 0;

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject || hit.transform.IsChildOf(transform)) continue;
            Vector3 toObj = hit.transform.position - transform.position;
            float dist = toObj.magnitude;
            Vector3 awayDir = -toObj.normalized;

            if (hit.CompareTag("Shell"))
            {
                ShellController s = hit.GetComponent<ShellController>();
                float avoidRadius = enemyData != null ? enemyData.shellAvoidRadius : 3.0f;
                if (s != null && s.Owner != gameObject && dist < avoidRadius) { totalAvoidVec += awayDir * (1.0f - (dist / avoidRadius)) * 3.0f; dangerCount++; }
            }
            else if (hit.CompareTag("Mine"))
            {
                TeamType mineTeam = TeamType.Neutral;
                MineController mc = hit.GetComponent<MineController>();
                if (mc != null) mineTeam = mc.GetTeam();
                else { RobotBombController rc = hit.GetComponent<RobotBombController>(); if (rc != null) mineTeam = rc.GetTeam(); }

                bool isAlly = (mineTeam == tankStatus.team);
                float avoidRadius = isAlly ? (enemyData != null ? enemyData.allyMineAvoidRadius : 2.0f) : (enemyData != null ? enemyData.mineAvoidRadius : 3.0f);
                if (avoidRadius > 0 && dist < avoidRadius) { totalAvoidVec += awayDir * (1.0f - (dist / avoidRadius)) * 2.0f; dangerCount++; }
            }
        }
        return dangerCount > 0 ? totalAvoidVec.normalized : Vector3.zero;
    }

    private void ThinkTarget()
    {
        var targets = FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Where(t => t.team != tankStatus.team && !t.IsDead).ToList();
        _currentTarget = targets.OrderBy(t => Vector3.Distance(transform.position, t.transform.position)).FirstOrDefault();
    }

    // ==========================================
    // ★修正: 砲塔は常にターゲット（またはスマートエイムの方向）を向き続ける
    // ==========================================
    private void HandleTurretAI()
    {
        if (turretTransform == null) return;
        Vector3 targetDir = Vector3.forward;

        if (_currentTarget != null)
        {
            targetDir = (_currentTarget.transform.position - turretTransform.position).normalized;
            if (enemyData != null && enemyData.useSmartRicochet)
            {
                _smartAimTimer -= Time.deltaTime;
                if (_smartAimTimer <= 0f) { _smartAimDir = FindSmartRicochetDirection(); _smartAimTimer = 0.1f; }
                if (_smartAimDir != Vector3.zero) targetDir = _smartAimDir;
            }
        }
        else targetDir = transform.forward;
        targetDir.y = 0;

        float offsetAngle = 0f;

        // クールタイム中や硬直中でなければ発射判定を行う
        bool canShootMain = false;
        bool canShootSub = false;

        if (!_isActionRigid && !_isActionBusy && _fireCooldownTimer <= 0f)
        {
            canShootMain = CheckShootTrajectory();

            if (!canShootMain && _smartAimDir == Vector3.zero)
            {
                _turretNoiseTime += Time.deltaTime * 0.8f;
                float searchAngle = enemyData != null ? enemyData.turretSearchAngle : 30f;
                offsetAngle = (Mathf.PerlinNoise(_turretNoiseTime, 0f) * 2.0f - 1.0f) * (searchAngle + 30f);
            }

            bool isSkillReady = (tankStatus.CurrentHp < tankStatus.GetData().maxHp * 0.5f) && (_skillTimer <= 0);
            if (isSkillReady && useSkillOnSubCannonHit) canShootSub = CheckSubCannonTrajectory();

            if (isSkillReady)
            {
                if (useSkillOnSubCannonHit) { if (canShootMain || canShootSub) StartCoroutine(FireSkillRoutine()); }
                else { if (canShootMain) { if (Random.value < skillChanceInLine) StartCoroutine(FireSkillRoutine()); else if (_currentAmmo > 0) StartCoroutine(FireMainBurstRoutine()); } }
            }
            else if (canShootMain && _currentAmmo > 0) StartCoroutine(FireMainBurstRoutine());
        }

        // 常に砲塔を回転させる（硬直中・クールタイム中も含む）
        if (targetDir != Vector3.zero)
        {
            Quaternion finalRot = Quaternion.LookRotation(targetDir) * Quaternion.Euler(0, offsetAngle, 0);
            float rotSpeed = enemyData != null ? enemyData.turretRotationSpeed : 60f;
            _independentTurretRotation = Quaternion.RotateTowards(_independentTurretRotation, finalRot, rotSpeed * Time.deltaTime);
            turretTransform.rotation = _independentTurretRotation;
        }
    }

    // ==========================================
    // ★修正: 主砲の射線判定。SimulateRaycastTrajectory の最後に true (自分への被弾を避ける) を渡す
    // ==========================================
    private bool CheckShootTrajectory()
    {
        if (_currentTarget == null || mainCannon.firePoints == null || mainCannon.firePoints.Length == 0) return false;
        Transform fp = mainCannon.firePoints[0];
        Vector3 startPos = fp.position;
        Vector3 dir = fp.forward;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");
        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;

        if (Physics.CheckSphere(startPos, checkRadius, LayerMask.GetMask("Wall"))) return false;
        if (Physics.Linecast(turretTransform.position, startPos, LayerMask.GetMask("Wall"))) return false;

        int maxBounces = (mainCannon.shellPrefab?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (enemyData == null || !enemyData.considerReflection) maxBounces = 0;

        if (enemyData != null && enemyData.useSmartRicochet && _smartAimDir != Vector3.zero)
        {
            if (Vector3.Angle(dir, _smartAimDir) <= enemyData.shotAllowAngle)
            {
                // avoidSelf = true
                if (SimulateRaycastTrajectory(startPos, _smartAimDir, maxBounces, layerMask, 0, true)) return true;
                else { _smartAimDir = Vector3.zero; _smartAimTimer = 0f; return false; }
            }
            return false;
        }
        // avoidSelf = true
        return SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0, true);
    }

    // ==========================================
    // ★修正: 副砲の射線判定。こちらは自爆を気にしないので false を渡す
    // ==========================================
    private bool CheckSubCannonTrajectory()
    {
        if (_currentTarget == null || subCannon.firePoints == null || subCannon.firePoints.Length == 0) return false;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");
        int maxBounces = (subCannon.shellPrefab?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (enemyData == null || !enemyData.considerReflection) maxBounces = 0;
        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;

        foreach (var fp in subCannon.firePoints)
        {
            if (fp == null) continue;
            if (Physics.CheckSphere(fp.position, checkRadius, LayerMask.GetMask("Wall"))) continue;
            // avoidSelf = false
            if (SimulateRaycastTrajectory(fp.position, fp.forward, maxBounces, layerMask, 0, false)) return true;
        }
        return false;
    }

    private IEnumerator FireMainBurstRoutine()
    {
        _isActionBusy = true;
        _isActionRigid = true; // 移動・地雷不可
        _currentAmmo--;

        for (int i = 0; i < 3; i++)
        {
            if (tankStatus.IsDead || tankStatus.IsInStun) break;

            // 連射中は毎発の射線チェックをせず、問答無用で発射する
            PerformMainShot();

            yield return new WaitForSeconds(mainCannon.burstInterval);
        }

        yield return new WaitForSeconds(mainCannon.shotDelay);

        if (_currentAmmo < tankStatus.GetData().maxAmmo) _currentAmmo++;

        // エネミーデータのクールタイムを適用
        _fireCooldownTimer = enemyData != null ? enemyData.fireCooldown : 1.0f;

        _isActionRigid = false;
        _isActionBusy = false;
    }

    // ==========================================
    // ★修正: スキル使用後（副砲発射後）の硬直中も、砲塔が敵を追い続けるようにする
    // ==========================================
    private IEnumerator FireSkillRoutine()
    {
        _isActionBusy = true;
        _isActionRigid = true; // 移動・地雷不可

        if (CheckShootTrajectory()) PerformMainShot();

        foreach (var fp in subCannon.firePoints)
        {
            if (fp == null) continue;
            GameObject shellObj = Instantiate(subCannon.shellPrefab, fp.position, fp.rotation);
            shellObj.GetComponent<ShellController>()?.Launch(gameObject, 0);
            if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(fp);
        }

        ResetSkillTimer();
        yield return new WaitForSeconds(subCannon.shotDelay);

        // ★追加: エネミーデータのクールタイムを適用
        _fireCooldownTimer = enemyData != null ? enemyData.fireCooldown : 1.0f;

        _isActionRigid = false;
        _isActionBusy = false;
    }

    private void PerformMainShot()
    {
        foreach (var fp in mainCannon.firePoints)
        {
            if (fp == null) continue;
            GameObject shellObj = Instantiate(mainCannon.shellPrefab, fp.position, fp.rotation);
            shellObj.GetComponent<ShellController>()?.Launch(gameObject, 0);
            if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(fp);
        }
    }

    private void ThinkMine()
    {
        if (tankStatus.isDevilMineLeaker) return;
        if (enemyData == null || !enemyData.useMine) return;
        if (tankStatus.ActiveMineCount >= tankStatus.GetData().maxMines) return;
        if (Physics.OverlapSphere(transform.position, enemyData.minePlacementSpacing).Any(c => c.CompareTag("Mine"))) return;
        if (Random.value < 0.02f) StartCoroutine(MineRoutine());
    }

    private IEnumerator MineRoutine()
    {
        _isActionRigid = true;
        GameObject prefabToUse = minePrefab != null ? minePrefab : tankStatus.GetMinePrefab();

        if (prefabToUse != null)
        {
            GameObject mineObj = Instantiate(prefabToUse, transform.position, Quaternion.identity);
            if (mineObj.TryGetComponent(out MineController mineCtrl)) { mineCtrl.Init(tankStatus, tankStatus.GetMineData()); tankStatus.OnMinePlaced(); }
            else if (mineObj.TryGetComponent(out RobotBombController robotBomb)) { robotBomb.Init(tankStatus, tankStatus.GetMineData()); tankStatus.OnMinePlaced(); }
            else if (mineObj.TryGetComponent(out TankSpawnerBox spawnerBox)) { spawnerBox.Init(tankStatus, tankStatus.team); tankStatus.OnMinePlaced(); }
        }

        yield return new WaitForSeconds(0.3f);

        if (_agent != null && _agent.isOnNavMesh)
        {
            Vector3 awayDir = -transform.forward;
            Vector3 escapeTarget = transform.position + awayDir * 5.0f + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));
            if (NavMesh.SamplePosition(escapeTarget, out NavMeshHit hit, 4.0f, NavMesh.AllAreas))
            {
                _moveTarget = hit.position;
                _agent.SetDestination(_moveTarget);
                _moveTimer = 0f;
            }
        }
        _isActionRigid = false;
    }

    // ==========================================
    // ★修正: スマートエイム判定。主砲用なので true (自分への被弾を避ける) を渡す
    // ==========================================
    private Vector3 FindSmartRicochetDirection()
    {
        if (mainCannon.firePoints == null || mainCannon.firePoints.Length == 0 || _currentTarget == null) return Vector3.zero;
        int maxBounces = (mainCannon.shellPrefab?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (maxBounces <= 0 || enemyData == null || !enemyData.considerReflection) return Vector3.zero;

        Transform fp = mainCannon.firePoints[0];
        Vector3 startPos = fp.position;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        Vector3 baseDir = (_currentTarget.transform.position - startPos).normalized;
        baseDir.y = 0;
        if (baseDir == Vector3.zero) baseDir = transform.forward;

        for (int angle = 0; angle <= 180; angle += 3)
        {
            Vector3 rightDir = Quaternion.Euler(0, angle, 0) * baseDir;
            if (SimulateRaycastTrajectory(startPos, rightDir, maxBounces, layerMask, 0, true)) return rightDir;
            if (angle != 0 && angle != 180)
            {
                Vector3 leftDir = Quaternion.Euler(0, -angle, 0) * baseDir;
                if (SimulateRaycastTrajectory(startPos, leftDir, maxBounces, layerMask, 0, true)) return leftDir;
            }
        }
        return Vector3.zero;
    }

    // ==========================================
    // ★修正: 射線シミュレーション本体。avoidSelf フラグを追加
    // ==========================================
    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce, bool avoidSelf = false)
    {
        if (currentBounce > 15) return false;
        dir.y = 0; dir.Normalize();
        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;

        RaycastHit[] hits = Physics.SphereCastAll(startPos, checkRadius, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.distance == 0) continue;

            // 自分の車体に当たった場合
            if (hit.collider.transform.IsChildOf(transform))
            {
                // 跳弾後に自分に向かってくる軌道ならNG
                if (avoidSelf && currentBounce > 0) return false;
                continue;
            }

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                if (bouncesLeft > 0)
                {
                    Vector3 reflectDir = Vector3.Reflect(dir, hit.normal);
                    reflectDir.y = 0; reflectDir.Normalize();
                    return SimulateRaycastTrajectory(hit.point + hit.normal * 0.05f, reflectDir, bouncesLeft - 1, layerMask, currentBounce + 1, avoidSelf);
                }
                return false; // 壁に当たって跳ね返れないならNG
            }

            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null) return hitTank.team != tankStatus.team; // 敵ならOK、味方ならNG
        }
        return false;
    }

    private void ResetSkillTimer() { _skillTimer = skillBaseCooldown + Random.Range(0f, skillRandomVariance); }
}