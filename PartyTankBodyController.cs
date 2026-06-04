using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// パーティタンクの「本体（前衛）」を制御するコントローラー。
/// 敵に接近し、5連装砲や火炎放射による強力な攻撃を行います。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(NavMeshAgent), typeof(LineRenderer))]
public class PartyTankBodyController : MonoBehaviour
{
    [Header("基本設定")]
    [Tooltip("本体の TankStatus")]
    public TankStatus mainTankStatus;
    [Tooltip("移動・射撃・AI 用の EnemyData")]
    public EnemyData mainEnemyData;

    [Header("コアとの紐付け設定")]
    [Tooltip("紐付け先のコアタンク（インスペクターでアタッチするとラインが引かれます）")]
    public PartyTankCoreController coreTank;
    [Tooltip("牽引ラインの描画に使用するマテリアル")]
    public Material towLineMaterial;

    [Header("武装：5Way砲塔")]
    [Tooltip("メイン砲塔の Transform")]
    public Transform mainTurret;
    [Tooltip("各砲塔の発射位置")]
    public Transform[] mainFirePoints;
    [Tooltip("マズルフラッシュの位置（未設定時は発射位置を使用）")]
    public Transform[] mainMuzzleFlashPoints;
    [Tooltip("5連装で撃つ弾のプレハブ")]
    public GameObject mainShellPrefab;

    [Header("武装：火炎放射")]
    [Tooltip("火炎のノズル位置（Forward が噴射方向）")]
    public Transform flamethrowerPoint;
    [Tooltip("敵を感知して火炎放射を開始する距離")]
    public float flameDetectRadius = 8.0f;
    [Tooltip("火炎放射を継続する時間（秒）")]
    public float flameDuration = 3.0f;
    [Tooltip("火炎放射終了後、次に使えるまでの待ち時間（秒）")]
    public float flameCooldown = 6.0f;
    [Tooltip("火炎弾のプレハブ（ShellData で isFlamethrower / ignoreExplosionsAndShells を有効に）")]
    public GameObject flameShellPrefab;
    [Tooltip("1秒あたりの火炎弾発射数")]
    public float flameFireRate = 10f;
    [Tooltip("ノズルモデルが90度ずれている場合のY軸補正（度）")]
    public float flameAimYawOffset = 0f;
    [Tooltip("火炎放射中も移動と通常射撃を行うか")]
    public bool moveWhileFlaming = false;

    [Header("アニメーション")]
    [Tooltip("左側の車輪")]
    public Transform[] mainLeftWheels;
    [Tooltip("右側の車輪")]
    public Transform[] mainRightWheels;
    [Tooltip("前進・後退時の車輪回転倍率")]
    public float wheelMoveSpinSpeed = 500f;
    [Tooltip("旋回時の車輪回転倍率")]
    public float wheelTurnSpinSpeed = 0.5f;

    [Header("自弾との衝突保護")]
    [Tooltip("自弾と本体の衝突を無視する時間（秒）")]
    public float selfShellIgnoreTime = 0.2f;

    [Header("回避設定")]
    [Tooltip("壁回避の判定半径")]
    public float wallAvoidRadius = 5.0f;

    [Header("必殺技（低HP時）")]
    [Tooltip("最大HPに対する割合。この以下で必殺モード")]
    [Range(0.05f, 0.95f)]
    public float ultimateHpThreshold = 0.35f;
    [Tooltip("必殺モード時に1080度回転攻撃へ入る確率（1=毎回、0.3=30%）")]
    [Range(0f, 1f)]
    public float ultimateSpinEnterChance = 0.35f;
    [Tooltip("必殺回転中の連射間隔（秒）の最小")]
    public float ultimateFireIntervalMin = 0.3f;
    [Tooltip("必殺回転中の連射間隔（秒）の最大")]
    public float ultimateFireIntervalMax = 0.5f;

    // 内部変数
    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private LineRenderer _lineRenderer;
    private List<LineRenderer> _trajectoryLineRenderers = new List<LineRenderer>();
    private TankStatus _currentTarget;

    private Vector3 _moveTarget;
    private float _moveTimer;
    private float _stuckTimer;
    private Vector3 _smoothedMoveDir;

    private float _currentFireCooldown;
    private float _currentFlameCooldown;
    private int _ammoCount;
    private float _shotRigidTimer;

    private bool _isSpinningMode = false;
    private bool _isUltimateSpinBurst = false;
    private bool _isFlaming = false;
    private TankStatus _flameLockTarget;
    private Quaternion _turretRotation;
    private float _turretNoiseTime;

    private bool IsGameActive =>
        GameManager.Instance != null
        && GameManager.Instance.IsGameStarted
        && !GameManager.Instance.IsGameFinished();

    private Vector3 _lastPos;
    private float _lastYRot;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
        _lineRenderer = GetComponent<LineRenderer>();

        if (mainTankStatus == null) mainTankStatus = GetComponent<TankStatus>();

        // ★追加: クラス名変更等でインスペクターの参照が外れていた場合の自動取得
        if (coreTank == null)
        {
            coreTank = transform.parent != null ? transform.parent.GetComponentInChildren<PartyTankCoreController>() : GetComponentInChildren<PartyTankCoreController>();
        }

        if (_agent != null)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }

    }

    private void Start()
    {
        AdjustSpawnHeight();
        DisableWheelColliders(mainLeftWheels);
        DisableWheelColliders(mainRightWheels);

        _lineRenderer.positionCount = 2;
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.startWidth = 0.15f;
        _lineRenderer.endWidth = 0.15f;
        if (towLineMaterial != null) _lineRenderer.material = towLineMaterial;

        if (mainFirePoints != null)
        {
            for (int i = 0; i < mainFirePoints.Length; i++)
            {
                GameObject trajObj = new GameObject($"TrajectoryLine_{i}");
                trajObj.transform.SetParent(transform);
                trajObj.transform.localPosition = Vector3.zero;
                LineRenderer lr = trajObj.AddComponent<LineRenderer>();
                lr.enabled = false;
                lr.startWidth = 0.1f;
                lr.endWidth = 0.1f;
                _trajectoryLineRenderers.Add(lr);
            }
        }

        if (mainTurret != null) _turretRotation = mainTurret.rotation;
        _ammoCount = mainTankStatus.GetTotalMaxAmmo();

        // ★追加: コアタンクのチームを本体のチームと同期させる
        if (coreTank != null && coreTank.coreTankStatus != null)
        {
            coreTank.coreTankStatus.SetTeam(mainTankStatus.team);
        }

        _lastPos = transform.position;
        _lastYRot = transform.eulerAngles.y;

        DecideNextMoveTarget();
        StartCoroutine(TurretBehaviorRoutine());
    }

    private void Update()
    {
        if (mainTankStatus.IsDead || !IsGameActive)
        {
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            foreach (var lr in _trajectoryLineRenderers) { if (lr != null) lr.enabled = false; }
            _isSpinningMode = false;
            _isUltimateSpinBurst = false;
            if (_rb != null) _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            return;
        }

        DrawConnectionLine();
        ThinkTarget();
        ThinkMoveLogic();
        HandleTurretLogic();
        CheckAndUseFlamethrower();
        UpdateWheelRotation();

        if (_currentFireCooldown > 0) _currentFireCooldown -= Time.deltaTime;
        if (_currentFlameCooldown > 0) _currentFlameCooldown -= Time.deltaTime;
        if (_shotRigidTimer > 0f) _shotRigidTimer -= Time.deltaTime;
    }

    private void FixedUpdate()
    {
        if (mainTankStatus.IsDead || mainTankStatus.IsInStun || !IsGameActive)
        {
            if (_rb != null) _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            if (_agent != null && _agent.isOnNavMesh) _agent.nextPosition = _rb.position;
            return;
        }

        if (_agent != null && !_agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(_rb.position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
        }

        ExecuteMovement();
    }

    private void LateUpdate()
    {
        if (mainTankStatus.IsDead || mainTankStatus.IsInStun || !IsGameActive) return;

        if (_isSpinningMode && mainTurret != null)
        {
            mainTurret.localRotation = Quaternion.Inverse(transform.rotation) * _turretRotation;
        }

        if (DebugVisualizer.Instance != null && mainFirePoints != null && mainTankStatus != null)
        {
            GameObject shellToUse = mainShellPrefab != null ? mainShellPrefab : mainTankStatus.GetShellPrefab();
            int bounces = mainTankStatus.GetRicochetCountForPrefab(shellToUse);
            if (mainEnemyData != null && !mainEnemyData.considerReflection) bounces = 0;

            for (int i = 0; i < mainFirePoints.Length; i++)
            {
                if (i < _trajectoryLineRenderers.Count && _trajectoryLineRenderers[i] != null && mainFirePoints[i] != null)
                {
                    DebugVisualizer.Instance.DrawTrajectoryLine(_trajectoryLineRenderers[i], mainFirePoints[i].position, mainFirePoints[i].forward, bounces);
                }
            }
        }
    }

    private void AdjustSpawnHeight()
    {
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10.0f, NavMesh.AllAreas))
        {
            float offsetY = 0f;
            Collider[] cols = GetComponentsInChildren<Collider>();
            float minColY = cols.Where(c => !c.isTrigger).Select(c => c.bounds.min.y).DefaultIfEmpty(float.MaxValue).Min();
            if (minColY != float.MaxValue) offsetY = transform.position.y - minColY;

            Vector3 groundPos = new Vector3(transform.position.x, hit.position.y + offsetY + 0.05f, transform.position.z);
            transform.position = groundPos;
            if (_agent != null && _agent.enabled) _agent.Warp(groundPos);
        }
    }

    private void DisableWheelColliders(Transform[] wheels)
    {
        if (wheels == null) return;
        foreach (var w in wheels) if (w != null) foreach (var c in w.GetComponentsInChildren<Collider>()) c.isTrigger = true;
    }

    private void DrawConnectionLine()
    {
        if (coreTank != null && coreTank.coreTankStatus != null && !coreTank.coreTankStatus.IsDead)
        {
            _lineRenderer.enabled = true;
            _lineRenderer.SetPosition(0, transform.position + Vector3.up * 0.5f);
            _lineRenderer.SetPosition(1, coreTank.transform.position + Vector3.up * 0.5f);
        }
        else
        {
            _lineRenderer.enabled = false;
        }
    }

    private void UpdateWheelRotation()
    {
        // 実際の移動量から車輪の回転を計算（前後移動）
        Vector3 deltaPos = transform.position - _lastPos;
        float fwdMove = Vector3.Dot(deltaPos, transform.forward);
        
        // 実際の回転量から車輪の回転を計算（左右旋回）
        float deltaRot = Mathf.DeltaAngle(_lastYRot, transform.eulerAngles.y);

        _lastPos = transform.position;
        _lastYRot = transform.eulerAngles.y;

        // ★修正: 回転挙動中（旋回中）と移動中（前進中）の回転量をそれぞれの倍率で計算
        // fwdMoveは1フレームあたりの移動距離、deltaRotは1フレームあたりの回転角度
        float moveSpin = fwdMove * wheelMoveSpinSpeed;
        float turnSpin = deltaRot * wheelTurnSpinSpeed;

        // 1080度回転攻撃中の砲塔回転を車輪に反映させる（本体は回っていないが、その場で旋回しているように見せる）
        if (_isSpinningMode)
        {
            float turretSpinSpeed = mainEnemyData != null ? mainEnemyData.turretRotationSpeed : 180f;
            turnSpin = turretSpinSpeed * Time.deltaTime * wheelTurnSpinSpeed * 5f; // 砲塔の回転速度に合わせる
        }

        // 左の車輪：前進で正回転、右旋回で正回転
        if (mainLeftWheels != null) 
        {
            foreach (var w in mainLeftWheels) 
            {
                if (w != null) w.Rotate(moveSpin + turnSpin, 0f, 0f, Space.Self);
            }
        }
        
        // 右の車輪：前進で正回転、右旋回で逆回転
        if (mainRightWheels != null) 
        {
            foreach (var w in mainRightWheels) 
            {
                if (w != null) w.Rotate(moveSpin - turnSpin, 0f, 0f, Space.Self);
            }
        }
    }

    private void ThinkTarget()
    {
        _currentTarget = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            .Where(t => t != null && !t.IsDead && t.team != mainTankStatus.team && (coreTank == null || t != coreTank.coreTankStatus))
            .OrderBy(t => Vector3.Distance(transform.position, t.transform.position))
            .FirstOrDefault();
    }

    private void ThinkMoveLogic()
    {
        if (mainEnemyData == null || _agent == null) return;
        _moveTimer += Time.deltaTime;

        if (_agent.isOnNavMesh)
        {
            Vector3 finalDest = _moveTarget;

            if (mainEnemyData.aiType == EnemyData.AIType.Aggressive && _currentTarget != null)
            {
                // ★修正: 敵にまっすぐ向かうのではなく、ランダムな挙動を含めて距離を詰める
                if (_moveTimer > 2.0f || Vector3.Distance(transform.position, _moveTarget) < 2.0f)
                {
                    _moveTimer = 0f;
                    
                    // ターゲットへの方向と距離
                    Vector3 toTarget = _currentTarget.transform.position - transform.position;
                    float dist = toTarget.magnitude;
                    Vector3 dir = toTarget.normalized;
                    
                    // 進行方向にランダムな角度をつける（ジグザグに近づく）
                    float randomAngle = Random.Range(-40f, 40f);
                    Vector3 randomDir = Quaternion.Euler(0, randomAngle, 0) * dir;
                    
                    // 距離を詰める先の座標 (最大8m先まで)
                    Vector3 targetPos = transform.position + randomDir * Mathf.Min(dist, 8f);
                    
                    if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5.0f, NavMesh.AllAreas))
                    {
                        _moveTarget = hit.position;
                    }
                    else
                    {
                        _moveTarget = _currentTarget.transform.position;
                    }
                }
                finalDest = _moveTarget;
            }
            else if (_moveTimer > 5.0f || Vector3.Distance(transform.position, _moveTarget) < 2.0f)
            {
                DecideNextMoveTarget();
                finalDest = _moveTarget;
            }

            _agent.SetDestination(finalDest);
        }
    }

    private void DecideNextMoveTarget()
    {
        _moveTimer = 0f;
        for (int i = 0; i < 5; i++)
        {
            Vector2 randCircle = Random.insideUnitCircle * 15f;
            Vector3 randomPos = transform.position + new Vector3(randCircle.x, 0, randCircle.y);
            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 10.0f, NavMesh.AllAreas))
            {
                if (Vector3.Distance(transform.position, hit.position) > 3.0f)
                {
                    _moveTarget = hit.position;
                    return;
                }
            }
        }
        _moveTarget = transform.position + transform.forward * 5f;
    }

    private void ExecuteMovement()
    {
        if ((_shotRigidTimer > 0f || (_isFlaming && !moveWhileFlaming)) && !mainTankStatus.isDevilBerserk)
        {
            _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            if (_agent != null && _agent.isOnNavMesh) _agent.nextPosition = _rb.position;
            _stuckTimer = 0f;
            return;
        }

        Vector3 baseDir = Vector3.zero;
        if (_agent != null && _agent.isOnNavMesh)
        {
            if (_agent.pathPending)
            {
                baseDir = _smoothedMoveDir; // 経路計算中は現在の進行方向を維持
            }
            else
            {
                // NavMeshの次のコーナー（曲がり角）に向かうことで壁を正確に回避する
                Vector3 toSteering = _agent.steeringTarget - transform.position;
                toSteering.y = 0f;
                if (toSteering.magnitude > 0.1f)
                {
                    baseDir = toSteering.normalized;
                }
                else
                {
                    // 最終目的地に非常に近い場合のフォールバック
                    Vector3 toTarget = _moveTarget - transform.position;
                    toTarget.y = 0f;
                    if (toTarget.magnitude > 1.0f) baseDir = toTarget.normalized;
                }
            }
        }

        Vector3 finalDir = baseDir;

        // ★追加: 弾・地雷・壁避け
        Vector3 deadlyAvoid = GetAvoidanceVector("Deadly");
        if (deadlyAvoid != Vector3.zero) finalDir = (finalDir * 0.4f + deadlyAvoid * 3.0f).normalized;

        Vector3 wallAvoid = GetWallAvoidanceVector(wallAvoidRadius);
        if (wallAvoid != Vector3.zero) finalDir = (finalDir * 0.2f + wallAvoid * 5.0f).normalized;

        int obstacleMask = LayerMask.GetMask("Wall", "Spike");
        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.6f, finalDir.normalized, out RaycastHit sphereHit, 1.5f, obstacleMask))
        {
            Vector3 wallNormal = sphereHit.normal; wallNormal.y = 0;
            Vector3 slideVel = Vector3.ProjectOnPlane(finalDir, wallNormal);
            finalDir = slideVel.magnitude < 0.1f ? wallNormal : slideVel.normalized + wallNormal * 0.5f;
        }

        _smoothedMoveDir = Vector3.Lerp(_smoothedMoveDir == Vector3.zero ? transform.forward : _smoothedMoveDir, finalDir.normalized, Time.fixedDeltaTime * 5.0f).normalized;

        float targetAngle = Mathf.Atan2(_smoothedMoveDir.x, _smoothedMoveDir.z) * Mathf.Rad2Deg;
        float currentY = _rb.rotation.eulerAngles.y;
        float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, mainTankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime);
        _rb.MoveRotation(Quaternion.Euler(0f, nextAngle, 0f));

        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle));
        float moveScale = angleDiff > 90f ? 0f : (angleDiff > 45f ? 0.35f : (angleDiff > 20f ? 0.7f : 1f));
        if (mainTankStatus.isDevilBerserk) moveScale = 1f; // 暴走中は常に前進

        Vector3 vel = (Quaternion.Euler(0f, nextAngle, 0f) * Vector3.forward) * (mainTankStatus.GetCurrentMoveSpeed() * moveScale);
        _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);

        if (new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude < 0.15f)
        {
            _stuckTimer += Time.fixedDeltaTime;
            if (_stuckTimer > 1.0f) { DecideNextMoveTarget(); _stuckTimer = 0f; }
        }
        else
        {
            _stuckTimer = 0f;
        }

        if (_agent != null && _agent.isOnNavMesh) _agent.nextPosition = _rb.position;
    }

    // --- 回避ベクトル計算用のヘルパー関数 ---

    private Vector3 GetAvoidanceVector(string type)
    {
        float maxSearchRadius = 3.5f;
        if (mainEnemyData != null)
        {
            maxSearchRadius = Mathf.Max(maxSearchRadius, mainEnemyData.shellAvoidRadius, mainEnemyData.mineAvoidRadius, mainEnemyData.allyMineAvoidRadius);
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, maxSearchRadius);
        Vector3 avoidVec = Vector3.zero;

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject || hit.transform.IsChildOf(transform)) continue;

            Vector3 toObj = hit.transform.position - transform.position;
            float dist = toObj.magnitude;
            if (dist == 0) continue;

            Vector3 awayDir = -toObj.normalized;
            awayDir.y = 0;

            if (type == "Deadly")
            {
                if (hit.CompareTag("Shell"))
                {
                    float avoidRad = (mainEnemyData != null) ? mainEnemyData.shellAvoidRadius : 3.0f;
                    if (dist < avoidRad) avoidVec += awayDir * (1.0f - dist / avoidRad);
                }
                else if (hit.CompareTag("Mine"))
                {
                    TeamType mineTeam = TeamType.Neutral;
                    var mineCtrl = hit.GetComponent<MineController>();
                    if (mineCtrl != null) mineTeam = mineCtrl.GetTeam();
                    else
                    {
                        var robot = hit.GetComponent<RobotBombController>();
                        if (robot != null) mineTeam = robot.GetTeam();
                    }

                    float avoidRad = 3.0f;
                    if (mainEnemyData != null)
                    {
                        avoidRad = (mineTeam == mainTankStatus.team) ? mainEnemyData.allyMineAvoidRadius : mainEnemyData.mineAvoidRadius;
                    }

                    if (dist < avoidRad) avoidVec += awayDir * (1.0f - dist / avoidRad);
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
                float strength = 1.0f - (hit.distance / checkDist);
                avoidVec += hit.normal * strength;
            }
        }
        return avoidVec;
    }

    private bool IsInUltimateMode()
    {
        if (mainTankStatus == null) return false;
        TankStatusData data = mainTankStatus.GetData();
        if (data == null || data.maxHp <= 0) return false;
        return (float)mainTankStatus.CurrentHp / data.maxHp <= ultimateHpThreshold;
    }

    private bool ShouldEnterSpinMode()
    {
        if (!IsInUltimateMode()) return true;
        return Random.value <= ultimateSpinEnterChance;
    }

    private IEnumerator TurretBehaviorRoutine()
    {
        while (!mainTankStatus.IsDead)
        {
            while (!IsGameActive) yield return null;

            _isSpinningMode = false;
            _isUltimateSpinBurst = false;
            yield return new WaitForSeconds(Random.Range(4f, 6f));

            if (!IsGameActive) continue;
            if (_isFlaming && !moveWhileFlaming) continue;
            if (!ShouldEnterSpinMode()) continue;

            _isSpinningMode = true;
            _isUltimateSpinBurst = IsInUltimateMode();

            Quaternion spinStartWorld = mainTurret != null ? mainTurret.rotation : _turretRotation;
            _turretRotation = spinStartWorld;
            const float spinTotalDegrees = 1080f;
            float accumulatedSpin = 0f;
            float spinSpeed = mainEnemyData != null ? mainEnemyData.turretRotationSpeed : 180f;
            float spinTimer = 0f;
            float nextUltimateFireTime = 0f;

            while (accumulatedSpin < spinTotalDegrees && !mainTankStatus.IsDead && IsGameActive)
            {
                if (_isFlaming && !moveWhileFlaming) break;

                spinTimer += Time.deltaTime;
                float step = spinSpeed * Time.deltaTime;
                accumulatedSpin += step;
                _turretRotation = spinStartWorld * Quaternion.Euler(0f, accumulatedSpin, 0f);

                if (_isUltimateSpinBurst)
                {
                    if (spinTimer >= nextUltimateFireTime)
                    {
                        nextUltimateFireTime = spinTimer + Random.Range(ultimateFireIntervalMin, ultimateFireIntervalMax);
                        TryUltimateSpinFire();
                    }
                }
                else if (_currentFireCooldown <= 0 && (!(_isFlaming && !moveWhileFlaming)))
                {
                    TryFire5Way();
                }

                yield return null;
            }

            _isUltimateSpinBurst = false;
        }
    }

    private void HandleTurretLogic()
    {
        if (_isSpinningMode) return;

        if (_isFlaming && _currentTarget != null)
        {
            AimFlamethrowerAtTarget(200f * Time.deltaTime);
            if (_currentFireCooldown <= 0 && moveWhileFlaming) TryFire5Way();
            return;
        }

        if (mainTurret != null && _currentTarget != null)
        {
            Vector3 td = _currentTarget.transform.position - mainTurret.position;
            td.y = 0;
            if (td.magnitude > 0.01f)
            {
                float ty = Mathf.Atan2(td.x, td.z) * Mathf.Rad2Deg;
                mainTurret.rotation = Quaternion.Euler(0, Mathf.MoveTowardsAngle(mainTurret.eulerAngles.y, ty, (mainEnemyData != null ? mainEnemyData.turretRotationSpeed : 120f) * Time.deltaTime), 0);
            }
        }

        if (_currentFireCooldown <= 0 && (!(_isFlaming && !moveWhileFlaming))) TryFire5Way();
    }

    public void OnMaxAmmoIncreased()
    {
        _ammoCount = mainTankStatus.GetTotalMaxAmmo();
    }

    private const float NearTurretBlockRadius = 2f;

    private bool IsFriendlyOrPartyNearTurret()
    {
        if (mainTankStatus == null) return false;

        Vector3 center = mainTurret != null ? mainTurret.position : transform.position;
        Collider[] closeHits = Physics.OverlapSphere(center, NearTurretBlockRadius);
        foreach (var col in closeHits)
        {
            if (col.transform.IsChildOf(transform)) continue;
            if (coreTank != null && col.transform.IsChildOf(coreTank.transform)) return true;

            TankStatus ts = col.GetComponentInParent<TankStatus>();
            if (ts == null || ts.IsDead || ts == mainTankStatus) continue;
            if (coreTank != null && coreTank.coreTankStatus != null && ts == coreTank.coreTankStatus) return true;
            if (ts.team == mainTankStatus.team) return true;
        }
        return false;
    }

    private void TryUltimateSpinFire()
    {
        if (mainTurret == null || mainFirePoints == null || mainFirePoints.Length == 0 || _ammoCount <= 0) return;
        if (_isFlaming && !moveWhileFlaming) return;

        GameObject shellToUse = mainShellPrefab != null ? mainShellPrefab : mainTankStatus.GetShellPrefab();
        if (shellToUse == null) return;

        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");
        Vector3 turretCenter = mainTurret.position;
        float checkRadius = mainEnemyData != null ? mainEnemyData.raycastRadius : 0.25f;
        int wallMask = LayerMask.GetMask("Wall");

        bool firedAtLeastOne = false;
        foreach (var fp in mainFirePoints)
        {
            if (fp == null) continue;
            if (Physics.CheckSphere(fp.position, checkRadius, wallMask)) continue;
            if (Physics.Linecast(turretCenter, fp.position, wallMask)) continue;
            if (WillHitCoreOnFireLine(fp.position, fp.forward, layerMask)) continue;

            firedAtLeastOne = true;
            if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(fp);

            GameObject shellObj = Instantiate(shellToUse, fp.position, fp.rotation);
            if (shellObj.TryGetComponent(out ShellController shell)) shell.Launch(gameObject, 0);
            IgnoreShellCollisionTemporarily(shellObj, selfShellIgnoreTime);
        }

        if (firedAtLeastOne)
        {
            if (EffectManager.Instance != null) EffectManager.Instance.ShotSound();
            _ammoCount--;
            StartCoroutine(ReloadAmmoRoutine());
            _shotRigidTimer = mainTankStatus.GetData() != null ? mainTankStatus.GetData().shotDelay * 0.5f : 0.05f;
        }
    }

    private bool WillHitCoreOnFireLine(Vector3 startPos, Vector3 dir, int layerMask)
    {
        if (coreTank == null) return false;

        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return false;
        dir.Normalize();

        float radius = mainEnemyData != null ? mainEnemyData.raycastRadius : 0.25f;
        RaycastHit[] hits = Physics.SphereCastAll(startPos, radius, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.distance <= 0) continue;

            if (hit.collider.transform.IsChildOf(coreTank.transform)
                || (coreTank.coreTankStatus != null && hit.collider.GetComponentInParent<TankStatus>() == coreTank.coreTankStatus))
                return true;

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall") || hit.collider.CompareTag("Wall"))
                return false;
        }
        return false;
    }

    private void TryFire5Way()
    {
        if (_currentTarget == null || mainTurret == null || mainFirePoints == null || mainFirePoints.Length == 0 || (_isFlaming && !moveWhileFlaming) || _ammoCount <= 0) return;
        if (IsFriendlyOrPartyNearTurret()) return;

        GameObject shellToUse = mainShellPrefab != null ? mainShellPrefab : mainTankStatus.GetShellPrefab();
        if (shellToUse == null) return;

        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");
        Vector3 turretCenter = mainTurret != null ? mainTurret.position : transform.position;
        float checkRadius = mainEnemyData != null ? mainEnemyData.raycastRadius : 0.25f;
        int bounces = mainTankStatus.GetRicochetCountForPrefab(shellToUse);
        if (mainEnemyData != null && !mainEnemyData.considerReflection) bounces = 0;

        bool canShootGroup = false;
        foreach (var fp in mainFirePoints)
        {
            if (fp == null) continue;
            if (Physics.CheckSphere(fp.position, checkRadius, LayerMask.GetMask("Wall"))) continue;
            if (Physics.Linecast(turretCenter, fp.position, LayerMask.GetMask("Wall"))) continue;
            if (SimulateRaycastTrajectory(fp.position, fp.forward, bounces, layerMask, 0))
            {
                canShootGroup = true;
                break;
            }
        }

        if (!canShootGroup) return;

        bool firedAtLeastOne = false;
        foreach (var fp in mainFirePoints)
        {
            if (fp == null) continue;

            if (Physics.CheckSphere(fp.position, checkRadius, LayerMask.GetMask("Wall"))) continue;
            
            if (WillHitCoreOnFireLine(fp.position, fp.forward, layerMask)) continue;
            if (WillHitAllyDirectly(fp.position, fp.forward, layerMask)) continue;

            firedAtLeastOne = true;
            if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(fp);

            GameObject shellObj = Instantiate(shellToUse, fp.position, fp.rotation);
            if (shellObj.TryGetComponent(out ShellController shell)) shell.Launch(gameObject, 0);
            IgnoreShellCollisionTemporarily(shellObj, selfShellIgnoreTime);
        }

        if (firedAtLeastOne)
        {
            if (EffectManager.Instance != null) EffectManager.Instance.ShotSound();
            _currentFireCooldown = mainEnemyData != null ? mainEnemyData.fireCooldown : 2f;
            _ammoCount--;
            StartCoroutine(ReloadAmmoRoutine());
            _shotRigidTimer = mainTankStatus.GetData() != null ? mainTankStatus.GetData().shotDelay : 0.1f;
        }
    }

    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce)
    {
        if (currentBounce > 15) return false;
        dir.y = 0; dir.Normalize();

        float radius = mainEnemyData != null ? mainEnemyData.raycastRadius : 0.25f;
        RaycastHit[] hits = Physics.SphereCastAll(startPos, radius, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.distance <= 0) continue;

            // 自身のパーツへの当たり判定
            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null && hitTank == mainTankStatus) continue;

            bool isCore = coreTank != null && hit.collider.transform.IsChildOf(coreTank.transform);

            // 味方意識（TeamAware）のチェック: 跳弾前（currentBounce == 0）の射線に味方またはコアがいれば撃たない
            if (currentBounce == 0 && mainEnemyData != null && mainEnemyData.isTeamAware)
            {
                if (isCore) return false;

                if (hitTank != null && hitTank.team == mainTankStatus.team) return false;
            }
            
            // コアを貫通して敵を狙う設定ではない場合（通常は遮蔽物扱い）
            if (isCore) return false;

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall") || hit.collider.CompareTag("Wall"))
            {
                if (bouncesLeft <= 0) return false;
                Vector3 r = Vector3.Reflect(dir, hit.normal);
                r.y = 0; r.Normalize();
                return SimulateRaycastTrajectory(hit.point + hit.normal * 0.05f, r, bouncesLeft - 1, layerMask, currentBounce + 1);
            }

            if (hitTank != null) return hitTank.team != mainTankStatus.team;
        }
        return false;
    }

    private bool WillHitAllyDirectly(Vector3 startPos, Vector3 dir, int layerMask)
    {
        dir.y = 0; dir.Normalize();
        float radius = mainEnemyData != null ? mainEnemyData.raycastRadius : 0.25f;
        RaycastHit[] hits = Physics.SphereCastAll(startPos, radius, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.distance <= 0) continue;
            
            TankStatus maybeAlly = hit.collider.GetComponentInParent<TankStatus>();
            if (maybeAlly != null && maybeAlly == mainTankStatus) continue;

            bool isCore = coreTank != null && hit.collider.transform.IsChildOf(coreTank.transform);
            if (isCore) return true;

            if (maybeAlly != null)
            {
                if (maybeAlly.team == mainTankStatus.team) return true;
                else return false; // 敵に当たるならOK
            }

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall") || hit.collider.CompareTag("Wall"))
            {
                return false; // 壁に当たるなら味方には当たらない
            }
        }
        return false;
    }

    private IEnumerator ReloadAmmoRoutine()
    {
        float cooldown = mainTankStatus.GetData() != null ? mainTankStatus.GetData().ammoCooldown : 1.5f;
        yield return new WaitForSeconds(cooldown);
        if (_ammoCount < mainTankStatus.GetTotalMaxAmmo()) _ammoCount++;
    }

    private void IgnoreShellCollisionTemporarily(GameObject shellObj, float ignoreTime)
    {
        Collider[] sc = shellObj.GetComponentsInChildren<Collider>();
        Collider[] mc = GetComponentsInChildren<Collider>();
        foreach (var s in sc)
        {
            if (s != null)
            {
                foreach (var m in mc)
                {
                    if (m != null) Physics.IgnoreCollision(s, m, true);
                }
            }
        }
        StartCoroutine(RestoreShellCollisionRoutine(sc, mc, ignoreTime));
    }

    private IEnumerator RestoreShellCollisionRoutine(Collider[] sc, Collider[] mc, float delay)
    {
        yield return new WaitForSeconds(delay);
        foreach (var s in sc)
        {
            if (s != null)
            {
                foreach (var m in mc)
                {
                    if (m != null) Physics.IgnoreCollision(s, m, false);
                }
            }
        }
    }

    private void AimFlamethrowerAtTarget(float rotateSpeed = -1f)
    {
        TankStatus aimTarget = _isFlaming && _flameLockTarget != null ? _flameLockTarget : _currentTarget;
        if (flamethrowerPoint == null || aimTarget == null) return;

        Vector3 dir = aimTarget.transform.position - flamethrowerPoint.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        float yawOffset = flameAimYawOffset;
        Quaternion nozzleWorld = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);

        // ノズルが砲塔の子のとき、ローカル回転（90度オフセット等）を考慮して砲塔を回す
        if (mainTurret != null && flamethrowerPoint.IsChildOf(mainTurret))
        {
            Quaternion turretWorld = nozzleWorld * Quaternion.Inverse(flamethrowerPoint.localRotation);
            float targetYaw = turretWorld.eulerAngles.y;
            if (rotateSpeed < 0f)
                mainTurret.rotation = Quaternion.Euler(0f, targetYaw, 0f);
            else
                mainTurret.rotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(mainTurret.eulerAngles.y, targetYaw, rotateSpeed), 0f);
        }
        else
        {
            if (rotateSpeed < 0f)
                flamethrowerPoint.rotation = nozzleWorld;
            else
                flamethrowerPoint.rotation = Quaternion.RotateTowards(flamethrowerPoint.rotation, nozzleWorld, rotateSpeed);
        }
    }

    private TankStatus GetEnemyInFlameRange()
    {
        if (flamethrowerPoint == null) return null;
        Vector3 origin = flamethrowerPoint.position;
        return FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            .Where(t => t != null && !t.IsDead && t.team != mainTankStatus.team
                && (coreTank == null || t != coreTank.coreTankStatus)
                && Vector3.Distance(origin, t.transform.position) <= flameDetectRadius
                && HasFlameLineOfSight(origin, t.transform.position))
            .OrderBy(t => Vector3.Distance(origin, t.transform.position))
            .FirstOrDefault();
    }

    private bool HasFlameLineOfSight(Vector3 origin, Vector3 targetPos)
    {
        Vector3 start = origin + Vector3.up * 0.5f;
        Vector3 end = targetPos + Vector3.up * 0.5f;
        Vector3 delta = end - start;
        float dist = delta.magnitude;
        if (dist < 0.05f) return true;

        int wallMask = LayerMask.GetMask("Wall");
        float radius = mainEnemyData != null ? mainEnemyData.raycastRadius : 0.3f;
        return !Physics.SphereCast(start, radius, delta.normalized, out _, dist, wallMask);
    }

    private void CheckAndUseFlamethrower()
    {
        if (_isFlaming || _currentFlameCooldown > 0 || flamethrowerPoint == null || flameShellPrefab == null) return;

        TankStatus enemyInRange = GetEnemyInFlameRange();
        if (enemyInRange == null) return;

        _currentTarget = enemyInRange;
        StartCoroutine(FlamethrowerRoutine());
    }

    private void EmitFlameSphere()
    {
        if (flameShellPrefab == null || flamethrowerPoint == null) return;

        Vector3 spawnPos = flamethrowerPoint.position + flamethrowerPoint.forward * 0.8f;
        Quaternion spawnRot = flamethrowerPoint.rotation * Quaternion.Euler(0f, Random.Range(-4f, 4f), 0f);
        GameObject shellObj = Instantiate(flameShellPrefab, spawnPos, spawnRot);

        Collider[] shellCols = shellObj.GetComponentsInChildren<Collider>();
        Collider[] bodyCols = GetComponentsInChildren<Collider>();
        foreach (var sc in shellCols)
        {
            if (sc == null) continue;
            foreach (var bc in bodyCols)
            {
                if (bc != null) Physics.IgnoreCollision(sc, bc, true);
            }
        }

        IgnoreOtherFlameShellCollisions(shellObj);

        if (shellObj.TryGetComponent(out ShellController scCtrl))
            scCtrl.Launch(gameObject, 0);
    }

    private void IgnoreOtherFlameShellCollisions(GameObject newShell)
    {
        Collider[] newCols = newShell.GetComponentsInChildren<Collider>();
        ShellController[] allShells = FindObjectsByType<ShellController>(FindObjectsSortMode.None);
        foreach (var other in allShells)
        {
            if (other == null || other.gameObject == newShell) continue;
            if (other.Owner != gameObject) continue;
            if (other.shellData == null || !other.shellData.isFlamethrower) continue;

            Collider[] otherCols = other.GetComponentsInChildren<Collider>();
            foreach (var nc in newCols)
            {
                if (nc == null) continue;
                foreach (var oc in otherCols)
                {
                    if (oc != null) Physics.IgnoreCollision(nc, oc, true);
                }
            }
        }
    }

    private IEnumerator FlamethrowerRoutine()
    {
        _flameLockTarget = _currentTarget;
        _isFlaming = true;
        _isSpinningMode = false;
        float timer = 0f;
        float nextFireTime = 0f;

        try
        {
            while (timer < flameDuration && !mainTankStatus.IsDead && IsGameActive)
            {
                AimFlamethrowerAtTarget(200f * Time.deltaTime);

                if (timer >= nextFireTime)
                {
                    nextFireTime = timer + (1f / Mathf.Max(1f, flameFireRate));
                    EmitFlameSphere();
                }

                timer += Time.deltaTime;
                yield return null;
            }
        }
        finally
        {
            _flameLockTarget = null;
            _currentFlameCooldown = flameCooldown;
            _isFlaming = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                if (mainTankStatus != null) _agent.speed = mainTankStatus.GetCurrentMoveSpeed();
            }
            DecideNextMoveTarget();
        }
    }
}