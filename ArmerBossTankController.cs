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

    [Header("ボススキル設定")]
    [SerializeField] private float skillBaseCooldown = 5f;
    [SerializeField] private float skillRandomVariance = 2f;
    [SerializeField, Range(0, 1)] private float skillChanceInLine = 0.4f;

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
            if (_agent.enabled) _agent.Warp(transform.position);
        }

        DecideNextMoveTarget();

        if (tankStatus != null && tankStatus.GetData() != null) _currentAmmo = tankStatus.GetData().maxAmmo;
        else _currentAmmo = 1;

        ResetSkillTimer();
        _lastDebugMode = isDebugMode;
    }

    private void Update()
    {

        // ★修正: ゲーム終了チェック
        if (tankStatus.IsDead || (GameManager.Instance != null && GameManager.Instance.IsGameFinished()))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            return;
        }

        if (tankStatus.IsDead || (GameManager.Instance != null && GameManager.Instance.IsGameFinished()))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            return;
        }

        // モード切替監視
        if (_lastDebugMode != isDebugMode)
        {
            if (isDebugMode)
            {
                if (_agent != null) { _agent.isStopped = true; _agent.enabled = false; }
                _rb.isKinematic = false;
                _isActionBusy = false;
                _isActionRigid = false;
            }
            else
            {
                if (_agent != null) { _agent.enabled = true; _agent.Warp(transform.position); _agent.isStopped = false; }
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
        if (_rb == null || _rb.isKinematic) return;
        if (tankStatus.IsInStun) { _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0); return; }

        if (isDebugMode)
        {
            ExecuteDebugMovement();
        }
        else
        {
            if (_isActionRigid) { StopMovementImmediate(); return; }
            ExecuteMovement();
        }
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

    private void ExecuteDebugMovement()
    {
        if (_agent != null && _agent.enabled) _agent.enabled = false;

        if (_debugMoveInput.magnitude < 0.1f)
        {
            StopMovementImmediate();
            return;
        }

        Vector3 moveDir = new Vector3(_debugMoveInput.x, 0, _debugMoveInput.y).normalized;
        float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
        float currentY = transform.eulerAngles.y;
        float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetData().rotationSpeed * Time.fixedDeltaTime);
        _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

        if (Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle)) < 80.0f)
        {
            float speed = tankStatus.GetCurrentMoveSpeed();
            Vector3 targetVel = transform.forward * speed;
            Vector3 diff = targetVel - _rb.linearVelocity;
            diff.y = 0;
            _rb.AddForce(diff, ForceMode.VelocityChange);
        }
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

    private void ExecuteMovement()
    {
        if (_agent == null || !_agent.isOnNavMesh || !_agent.enabled) { StopMovementImmediate(); return; }
        if (_agent.pathPending) return;

        Vector3 desiredVel = _agent.desiredVelocity;
        Vector3 dangerDir = CalculateDangerAvoidance();
        if (dangerDir != Vector3.zero) desiredVel = dangerDir.normalized * tankStatus.GetCurrentMoveSpeed();

        // 壁ブレーキ
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f + transform.forward * 1.5f, transform.forward, 0.8f, LayerMask.GetMask("Wall")))
        {
            if (Vector3.Dot(desiredVel, transform.forward) > 0) { desiredVel = Vector3.zero; _rb.linearVelocity = Vector3.zero; }
        }

        if (desiredVel.magnitude > 0.1f)
        {
            Vector3 moveDir = desiredVel.normalized;
            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float currentY = transform.eulerAngles.y;
            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetData().rotationSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            if (Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle)) < 45.0f)
            {
                float speed = tankStatus.GetCurrentMoveSpeed();
                Vector3 diff = (transform.forward * speed) - _rb.linearVelocity;
                diff.y = 0;
                _rb.AddForce(diff * 20f, ForceMode.Acceleration);
            }
        }
        else StopMovementImmediate();

        _agent.nextPosition = _rb.position;
    }

    private void StopMovementImmediate()
    {
        Vector3 vel = _rb.linearVelocity;
        Vector3 horizontalVel = new Vector3(vel.x, 0, vel.z);
        _rb.AddForce(-horizontalVel * 5.0f, ForceMode.Acceleration);
    }

    private Vector3 CalculateDangerAvoidance()
    {
        float maxRadius = Mathf.Max(enemyData.shellAvoidRadius, enemyData.mineAvoidRadius, enemyData.allyMineAvoidRadius) + 2.0f;
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
                if (s != null && s.Owner != gameObject && dist < enemyData.shellAvoidRadius)
                {
                    totalAvoidVec += awayDir * (1.0f - (dist / enemyData.shellAvoidRadius)) * 3.0f;
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
                float avoidRadius = isAlly ? enemyData.allyMineAvoidRadius : enemyData.mineAvoidRadius;
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
        bool canShoot = CheckShootTrajectory();
        Vector3 targetDir;

        if (_currentTarget != null)
        {
            if (canShoot) targetDir = (_currentTarget.transform.position - turretTransform.position).normalized;
            else
            {
                _turretNoiseTime += Time.deltaTime * 0.5f;
                float noise = Mathf.PerlinNoise(_turretNoiseTime, 0f) * 2.0f - 1.0f;
                targetDir = Quaternion.Euler(0, noise * enemyData.turretSearchAngle, 0) * (_currentTarget.transform.position - turretTransform.position).normalized;
            }
        }
        else
        {
            _turretNoiseTime += Time.deltaTime * 0.5f;
            targetDir = Quaternion.Euler(0, (Mathf.PerlinNoise(_turretNoiseTime, 0f) * 2.0f - 1.0f) * enemyData.turretSearchAngle, 0) * transform.forward;
        }

        targetDir.y = 0;
        if (targetDir != Vector3.zero) turretTransform.rotation = Quaternion.RotateTowards(_independentTurretRotation, Quaternion.LookRotation(targetDir), enemyData.turretRotationSpeed * Time.deltaTime);
        _independentTurretRotation = turretTransform.rotation;

        if (!_isActionRigid && !_isActionBusy && canShoot)
        {
            if ((tankStatus.CurrentHp < tankStatus.GetData().maxHp * 0.5f) && _skillTimer <= 0 && Random.value < skillChanceInLine) StartCoroutine(FireSkillRoutine());
            else if (_currentAmmo > 0) StartCoroutine(FireMainBurstRoutine());
        }
    }

    // ArmerBossTankController.cs 内の CheckShootTrajectory を差し替え

    private bool CheckShootTrajectory()
    {
        if (_currentTarget == null || mainCannon.firePoints == null || mainCannon.firePoints.Length == 0) return false;

        // 代表として最初の発射口を使う
        Transform fp = mainCannon.firePoints[0];
        Vector3 startPos = fp.position;
        Vector3 dir = fp.forward;
        float dist = 100f;

        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        // --- ★追加: 近距離の味方チェック ---
        // ボスの場合、砲塔が大きいので範囲を少し広め(1.0f)にとる
        // ボスも enemyData.isTeamAware に従うか、ボスは問答無用で撃つかですが、
        // 変数がある以上は従う形にします。
        if (enemyData != null && enemyData.isTeamAware)
        {
            Collider[] closeHits = Physics.OverlapSphere(startPos, 1.0f, layerMask);
            foreach (var hit in closeHits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                TankStatus closeTank = hit.GetComponentInParent<TankStatus>();
                if (closeTank != null && closeTank.team == tankStatus.team)
                {
                    return false; // 味方が近すぎる
                }
            }
        }

        // --- ★修正: 貫通チェック ---
        RaycastHit[] hits = Physics.SphereCastAll(startPos, 0.3f, dir, dist, layerMask); // ボスの弾は太いと仮定して半径0.4f
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;

            // 壁判定 (ボスは反射を考慮しないロジックのままとする、または必要なら追加)
            // 以前のコードでもLinecastで壁判定をしていたが、SphereCastAll内で壁に当たったらそこで終了とする
            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                return false;
            }

            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null)
            {
                if (hitTank.team != tankStatus.team)
                {
                    return true; // 敵発見
                }
                else
                {
                    // 味方発見
                    if (enemyData != null && enemyData.isTeamAware)
                    {
                        return false; // 射線妨害
                    }
                    // 配慮しないなら貫通して次へ
                }
            }
        }

        return false;
    }

    private void TryFireMain() { /* HandleDebugInputで直接呼ぶため未使用 */ }
    private void TryFireSkill() { /* HandleDebugInputで直接呼ぶため未使用 */ }

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
            shellObj.GetComponent<ShellController>()?.Launch(gameObject, 1);
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
        GameObject prefabToUse = tankStatus.GetMinePrefab();
        if (prefabToUse != null)
        {
            GameObject mineObj = Instantiate(prefabToUse, transform.position, Quaternion.identity);
            MineController mineCtrl = mineObj.GetComponent<MineController>();
            if (mineCtrl != null) { mineCtrl.Init(tankStatus, tankStatus.GetMineData()); tankStatus.OnMinePlaced(); }
            else { RobotBombController robotBomb = mineObj.GetComponent<RobotBombController>(); if (robotBomb != null) { robotBomb.Init(tankStatus, tankStatus.GetMineData()); tankStatus.OnMinePlaced(); } }
        }
        yield return null;
    }

    private void ResetSkillTimer() { _skillTimer = skillBaseCooldown + Random.Range(0f, skillRandomVariance); }
}