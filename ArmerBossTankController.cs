using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem; // ★必須: Input Systemを使うために追加
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
    // ★追加: スマートエイム用変数
    private Vector3 _smartAimDir = Vector3.zero;
    private float _smartAimTimer = 0f;

    [Header("ボススキル設定")]
    [SerializeField] private float skillBaseCooldown = 5f;
    [SerializeField] private float skillRandomVariance = 2f;
    [SerializeField, Range(0, 1)] private float skillChanceInLine = 0.4f;

    // ★追加: 副砲での索敵とスキル確定発動のチェックボックス
    [Tooltip("ONにすると、スキル発動可能時（HP半分以下＆クールダウン完了時）に、主砲だけでなく「副砲」の射線も計算し、副砲が当たる角度であれば確実にスキル（3way攻撃）を使用するようになります。")]
    [SerializeField] private bool useSkillOnSubCannonHit = true;

    // ステージサイズ
    private const float STAGE_LIMIT_X = 13.5f;
    private const float STAGE_LIMIT_Z = 13.5f;

    // コンポーネント
    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private LineRenderer _lineRenderer;

    // 内部変数
    private TankStatus _currentTarget;
    private Vector3 _moveTarget;
    private float _moveTimer;
    private float _stuckTimer;
    private bool _lastDebugMode = false;

    // デバッグ用変数
    private Vector2 _debugMoveInput;

    // アクション管理
    private bool _isActionBusy;
    private bool _isActionRigid;
    private int _currentAmmo;
    private float _skillTimer;

    // 砲塔制御
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

        // ★修正: ボスは大きくて地面にめり込みやすいため、スポーン時に広範囲で床を探して強制的に地上へ引き上げる
        if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out UnityEngine.AI.NavMeshHit navHit, 10.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            // 床の高さから少しだけ上（+0.5f）に浮かせてめり込みを完全に防ぐ
            transform.position = navHit.position + Vector3.up * 0.5f;
            if (_agent != null && _agent.enabled) _agent.Warp(transform.position);
        }

        DecideNextMoveTarget();

        if (tankStatus != null && tankStatus.GetData() != null) _currentAmmo = tankStatus.GetData().maxAmmo;
        else _currentAmmo = 1;

        ResetSkillTimer();
        _lastDebugMode = isDebugMode;
    }

    private void Update()
    {
        // ★修正: GameManagerのチェックやIsDeadの時に、NavMeshに乗っている(isOnNavMesh)時だけisStoppedを触るようにする
        if (tankStatus.IsDead || (GameManager.Instance != null && (!GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished())))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;

            // isOnNavMesh を追加してエラーを回避
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.isStopped = true;
            return;
        }
        else
        {
            // ここも isOnNavMesh を追加
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh && _agent.isStopped) _agent.isStopped = false;
        }

        // モード切替監視
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
            }
            _lastDebugMode = isDebugMode;
        }

        if (_skillTimer > 0) _skillTimer -= Time.deltaTime;

        if (isDebugMode)
        {
            HandleDebugInput();
        }
        else
        {
            ThinkTarget();
            HandleTurretAI();

            if (!_isActionRigid)
            {
                ThinkMove();
                ThinkMine();
            }
        }

        // デバッグライン
        if (DebugVisualizer.Instance != null && _lineRenderer != null)
        {
            if (mainCannon.firePoints != null && mainCannon.firePoints.Length > 0 && mainCannon.firePoints[0] != null)
            {
                int bounces = (mainCannon.shellPrefab?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
                DebugVisualizer.Instance.DrawTrajectoryLine(_lineRenderer, mainCannon.firePoints[0].position, mainCannon.firePoints[0].forward, bounces);
            }
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

    // --- デバッグ操作 (New Input System 対応) ---
    private void HandleDebugInput()
    {
        // キーボードがない場合は何もしない
        if (Keyboard.current == null) return;

        // --- 移動入力 (WASD or 矢印キー) ---
        float h = 0f;
        float v = 0f;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h = -1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h = 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v = 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v = -1f;

        _debugMoveInput = new Vector2(h, v);

        // --- 攻撃入力 ---

        // スペースキー: スキル
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            _isActionBusy = false;
            _isActionRigid = false;
            StartCoroutine(FireSkillRoutine());
        }

        // 左クリック or Zキー: 主砲
        bool firePressed = false;
        if (Mouse.current != null && Mouse.current.leftButton.isPressed) firePressed = true;
        if (Keyboard.current.zKey.isPressed) firePressed = true;

        if (firePressed)
        {
            if (_currentAmmo <= 0) _currentAmmo = tankStatus.GetData().maxAmmo;
            if (!_isActionBusy) StartCoroutine(FireMainBurstRoutine());
        }

        // Mキー: 地雷
        if (Keyboard.current.mKey.wasPressedThisFrame)
        {
            StartCoroutine(MineRoutine());
        }

        // --- 砲塔操作 (マウス追従) ---
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
        if (_isActionRigid || tankStatus.IsInStun)
        {
            StopMovementImmediate();
            return;
        }

        if (_agent == null || !_agent.isOnNavMesh || !_agent.enabled)
        {
            StopMovementImmediate();
            return;
        }

        Vector3 desiredVel = _agent.desiredVelocity;

        // ★修正: NavMeshが立ち止まりかけている時（角や狭い場所など）は、強制的に前進させてスタックを防ぐ
        if (desiredVel.magnitude < 0.1f)
        {
            desiredVel = transform.forward * tankStatus.GetCurrentMoveSpeed();

            // それでも動かないような完全スタック状態なら、目的地を強制的に再計算する
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

        // 1. 危険回避（弾・地雷）
        Vector3 dangerDir = CalculateDangerAvoidance();
        if (dangerDir != Vector3.zero)
        {
            Vector3 baseEscape = (desiredVel.magnitude > 0.1f ? desiredVel.normalized : transform.forward);
            desiredVel = Vector3.Lerp(baseEscape, dangerDir.normalized, 0.8f).normalized * tankStatus.GetCurrentMoveSpeed();
        }

        int obstacleMask = LayerMask.GetMask("Wall", "Spike");

        // 2. ヒゲセンサー（Ray）による滑らかな壁避けステアリング
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

        // 3. 究極のめり込み・スタック防止（球体スライダー）
        Vector3 checkDir = desiredVel.magnitude > 0.1f ? desiredVel.normalized : transform.forward;
        float bossRadius = 0.6f;

        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, bossRadius, checkDir, out RaycastHit sphereHit, 1.5f, obstacleMask))
        {
            Vector3 wallNormal = sphereHit.normal;
            wallNormal.y = 0;

            Vector3 slideVel = Vector3.ProjectOnPlane(desiredVel, wallNormal);

            if (slideVel.magnitude < 0.1f)
            {
                // 角にハマったら壁から押し出されて脱出する
                desiredVel = wallNormal * tankStatus.GetCurrentMoveSpeed();
            }
            else
            {
                // 通常のスライド
                desiredVel = (slideVel.normalized * tankStatus.GetCurrentMoveSpeed()) + (wallNormal * 1.5f);
            }
        }

        // 4. 滑らかな移動処理（止まらない）
        if (desiredVel.magnitude > 0.1f)
        {
            Vector3 moveDir = desiredVel.normalized;
            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float currentY = _rb.rotation.eulerAngles.y;

            float rotSpeed = tankStatus.GetCurrentRotationSpeed();
            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, rotSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            if (Mathf.Abs(Mathf.DeltaAngle(nextAngle, targetAngle)) <= 60.0f)
            {
                float speed = tankStatus.GetCurrentMoveSpeed();
                _rb.linearVelocity = new Vector3((transform.forward * speed).x, _rb.linearVelocity.y, (transform.forward * speed).z);
            }
            else
            {
                float speed = tankStatus.GetCurrentMoveSpeed() * 0.5f;
                _rb.linearVelocity = new Vector3((transform.forward * speed).x, _rb.linearVelocity.y, (transform.forward * speed).z);
            }
        }
        else
        {
            StopMovementImmediate();
        }

        if (_agent.isOnNavMesh) _agent.nextPosition = _rb.position;
    }

    // --- AI移動 (NavMesh) ---
    private void ThinkMove()
    {
        _moveTimer += Time.deltaTime;
        if (_agent != null && !_agent.enabled && !isDebugMode) { _agent.enabled = true; _agent.Warp(transform.position); }

        float distToDest = (_agent != null && _agent.isOnNavMesh && _agent.hasPath) ? _agent.remainingDistance : Vector3.Distance(transform.position, _moveTarget);
        if (distToDest < 3.0f || _moveTimer > 8.0f) { DecideNextMoveTarget(); _moveTimer = 0f; }

        if (_rb.linearVelocity.magnitude < 0.1f && !_isActionRigid)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer > 1.0f) { DecideNextMoveTarget(); _stuckTimer = 0f; }
        }
        else _stuckTimer = 0f;

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh) _agent.SetDestination(_moveTarget);
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
                if (s != null && s.Owner != gameObject && dist < avoidRadius)
                {
                    totalAvoidVec += awayDir * (1.0f - (dist / avoidRadius)) * 3.0f;
                    dangerCount++;
                }
            }
            else if (hit.CompareTag("Mine"))
            {
                TeamType mineTeam = TeamType.Neutral;
                MineController mc = hit.GetComponent<MineController>();
                if (mc != null) mineTeam = mc.GetTeam();
                else { RobotBombController rc = hit.GetComponent<RobotBombController>(); if (rc != null) mineTeam = rc.GetTeam(); }

                bool isAlly = (mineTeam == tankStatus.team);
                float avoidRadius = isAlly ? (enemyData != null ? enemyData.allyMineAvoidRadius : 2.0f) : (enemyData != null ? enemyData.mineAvoidRadius : 3.0f);
                if (avoidRadius > 0 && dist < avoidRadius)
                {
                    totalAvoidVec += awayDir * (1.0f - (dist / avoidRadius)) * 2.0f;
                    dangerCount++;
                }
            }
        }
        return dangerCount > 0 ? totalAvoidVec.normalized : Vector3.zero;
    }

    private void ThinkTarget()
    {
        var targets = FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Where(t => t.team != tankStatus.team && !t.IsDead).ToList();
        _currentTarget = targets.OrderBy(t => Vector3.Distance(transform.position, t.transform.position)).FirstOrDefault();
    }

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
                if (_smartAimTimer <= 0f)
                {
                    _smartAimDir = FindSmartRicochetDirection();
                    _smartAimTimer = 0.1f;
                }

                if (_smartAimDir != Vector3.zero)
                {
                    targetDir = _smartAimDir;
                }
            }
        }
        else
        {
            targetDir = transform.forward;
        }

        targetDir.y = 0;

        bool canShootMain = CheckShootTrajectory();
        bool canShootSub = false;

        float offsetAngle = 0f;
        if (!canShootMain && _smartAimDir == Vector3.zero)
        {
            // ボスも常に砲塔を動かして警戒
            _turretNoiseTime += Time.deltaTime * 0.8f;
            float noise = Mathf.PerlinNoise(_turretNoiseTime, 0f) * 2.0f - 1.0f;
            float searchAngle = enemyData != null ? enemyData.turretSearchAngle : 30f;
            offsetAngle = noise * (searchAngle + 30f);
        }

        if (targetDir != Vector3.zero)
        {
            Quaternion baseRot = Quaternion.LookRotation(targetDir);
            Quaternion finalRot = baseRot * Quaternion.Euler(0, offsetAngle, 0);
            float rotSpeed = enemyData != null ? enemyData.turretRotationSpeed : 60f;

            _independentTurretRotation = Quaternion.RotateTowards(_independentTurretRotation, finalRot, rotSpeed * Time.deltaTime);
            turretTransform.rotation = _independentTurretRotation;
        }

        if (!_isActionRigid && !_isActionBusy)
        {
            bool isSkillReady = (tankStatus.CurrentHp < tankStatus.GetData().maxHp * 0.5f) && (_skillTimer <= 0);

            if (isSkillReady && useSkillOnSubCannonHit)
            {
                canShootSub = CheckSubCannonTrajectory();
            }

            if (isSkillReady)
            {
                if (useSkillOnSubCannonHit)
                {
                    if (canShootMain || canShootSub) StartCoroutine(FireSkillRoutine());
                }
                else
                {
                    if (canShootMain)
                    {
                        if (Random.value < skillChanceInLine) StartCoroutine(FireSkillRoutine());
                        else if (_currentAmmo > 0) StartCoroutine(FireMainBurstRoutine());
                    }
                }
            }
            else if (canShootMain && _currentAmmo > 0)
            {
                StartCoroutine(FireMainBurstRoutine());
            }
        }
    }
    // ArmerBossTankController.cs 内の CheckShootTrajectory を差し替え

    [Tooltip("現在向いている方向に主砲を撃った場合、敵に当たるかを判定する")]
    private bool CheckShootTrajectory()
    {
        if (_currentTarget == null || mainCannon.firePoints == null || mainCannon.firePoints.Length == 0) return false;

        Transform fp = mainCannon.firePoints[0];
        Vector3 startPos = fp.position;
        Vector3 dir = fp.forward;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;
        // ★壁撃ち・透視防止の最終防衛線（ボス用）
        if (Physics.CheckSphere(startPos, checkRadius, LayerMask.GetMask("Wall"))) return false;
        if (Physics.Linecast(turretTransform.position, startPos, LayerMask.GetMask("Wall"))) return false;

        int maxBounces = (mainCannon.shellPrefab?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (enemyData == null || !enemyData.considerReflection) maxBounces = 0;

        if (enemyData != null && enemyData.useSmartRicochet && _smartAimDir != Vector3.zero)
        {
            float angleDiff = Vector3.Angle(dir, _smartAimDir);
            if (angleDiff <= enemyData.shotAllowAngle)
            {
                if (SimulateRaycastTrajectory(startPos, _smartAimDir, maxBounces, layerMask, 0))
                {
                    if (turretTransform != null)
                    {
                        turretTransform.rotation = Quaternion.LookRotation(_smartAimDir);
                        _independentTurretRotation = turretTransform.rotation;
                    }
                    return true;
                }
                else
                {
                    _smartAimDir = Vector3.zero;
                    _smartAimTimer = 0f;
                    return false;
                }
            }
            return false;
        }

        return SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0);
    }

    [Tooltip("副砲が敵に当たるか判定する")]
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
            Vector3 startPos = fp.position;
            Vector3 dir = fp.forward;

            if (Physics.CheckSphere(startPos, checkRadius, LayerMask.GetMask("Wall"))) continue;

            if (SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0))
            {
                return true;
            }
        }
        return false;
    }

    private IEnumerator FireMainBurstRoutine()
    {
        _isActionBusy = true; _isActionRigid = true; _currentAmmo--;
        for (int i = 0; i < 3; i++) { PerformMainShot(); yield return new WaitForSeconds(mainCannon.burstInterval); }
        yield return new WaitForSeconds(mainCannon.shotDelay);
        if (_currentAmmo < tankStatus.GetData().maxAmmo) _currentAmmo++;
        _isActionRigid = false; _isActionBusy = false;
    }

    private IEnumerator FireSkillRoutine()
    {
        _isActionBusy = true; _isActionRigid = true;
        PerformMainShot();
        foreach (var fp in subCannon.firePoints)
        {
            if (fp == null) continue;
            GameObject shellObj = Instantiate(subCannon.shellPrefab, fp.position, fp.rotation);
            shellObj.GetComponent<ShellController>()?.Launch(gameObject, 0);
            if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(fp);
        }
        ResetSkillTimer();
        yield return new WaitForSeconds(subCannon.shotDelay);
        _isActionRigid = false; _isActionBusy = false;
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
        if (enemyData == null || !enemyData.useMine) return;
        if (tankStatus.ActiveMineCount >= tankStatus.GetData().maxMines) return;
        if (Physics.OverlapSphere(transform.position, enemyData.minePlacementSpacing).Any(c => c.CompareTag("Mine"))) return;
        if (Random.value < 0.02f) StartCoroutine(MineRoutine());
    }

    private IEnumerator MineRoutine()
    {
        _isActionRigid = true; // ★硬直開始（移動と砲塔回転が一切止まる）
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

        // 硬直時間を待つ（0.3秒で素早く動けるようにする）
        yield return new WaitForSeconds(0.3f);

        // ★硬直が完全に終わってから、次の逃げ先を決定する
        if (_agent != null && _agent.isOnNavMesh)
        {
            // 自分が向いている方向とは反対側（後ろ）に逃げる基準を作る
            Vector3 awayDir = -transform.forward;

            // 現在地から4mほど離れた安全なランダム地点を探す
            Vector3 escapeTarget = transform.position + awayDir * 5.0f + new Vector3(Random.Range(-2f, 2f), 0, Random.Range(-2f, 2f));

            if (NavMesh.SamplePosition(escapeTarget, out NavMeshHit hit, 4.0f, NavMesh.AllAreas))
            {
                _moveTarget = hit.position;
                _agent.SetDestination(_moveTarget);
                _moveTimer = 0f; // 即座に更新させる

                // 次のフレームからExecuteMovement()が呼ばれ、
                // 車体を escapeTarget の方向へ向けてから走り出すようになる
            }
        }

        _isActionRigid = false; // ★硬直解除（ここから移動再開）
    }

    [Tooltip("扇状に広がりながら跳弾ルートをスキャンする（ボスの究極エイム）")]
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
            if (SimulateRaycastTrajectory(startPos, rightDir, maxBounces, layerMask, 0)) return rightDir;

            if (angle != 0 && angle != 180)
            {
                Vector3 leftDir = Quaternion.Euler(0, -angle, 0) * baseDir;
                if (SimulateRaycastTrajectory(startPos, leftDir, maxBounces, layerMask, 0)) return leftDir;
            }
        }
        return Vector3.zero;
    }

    [Tooltip("球(SphereCast)を飛ばして跳弾をシミュレーションする")]
    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce)
    {
        if (currentBounce > 15) return false;

        dir.y = 0;
        dir.Normalize();
        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;

        RaycastHit[] hits = Physics.SphereCastAll(startPos, checkRadius, dir, 100f, layerMask);
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
                    reflectDir.y = 0;
                    reflectDir.Normalize();
                    return SimulateRaycastTrajectory(hit.point + hit.normal * 0.05f, reflectDir, bouncesLeft - 1, layerMask, currentBounce + 1);
                }
                return false;
            }

            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null)
            {
                return hitTank.team != tankStatus.team;
            }
        }
        return false;
    }


    private void ResetSkillTimer() { _skillTimer = skillBaseCooldown + Random.Range(0f, skillRandomVariance); }


}