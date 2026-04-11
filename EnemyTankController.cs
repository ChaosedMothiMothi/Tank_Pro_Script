using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AI;

public class EnemyTankController : MonoBehaviour
{
    [Header("Settings")]
    public EnemyData enemyData;
    public TankStatus tankStatus;
    public Transform turretTransform;
    public Transform firePoint;

    [Header("Resources")]
    public GameObject minePrefab;

    // --- 内部コンポーネント ---
    private Rigidbody _rb;
    private LineRenderer _lineRenderer;
    private Collider[] _myColliders;
    private NavMeshAgent _agent;

    // --- 状態管理 ---
    private TankStatus _currentTarget;
    private Vector3 _moveTarget;
    private float _moveTimer;      // 同じターゲットへの移動時間
    private float _stuckTimer;     // スタック検知用
    private float _nextTargetUpdateTime = 0f;

    // --- 移動制御 ---
    private Vector3 _smoothedMoveDir;
    private const float STAGE_LIMIT = 13.5f; // 壁際判定のため少し狭める

    // --- 砲塔・攻撃制御 ---
    private float _turretNoiseTime;
    private Quaternion _independentTurretRotation;

    [Tooltip("硬直状態（攻撃中や地雷設置中など）を判定するフラグ")]
    private bool _isActionRigid = false;
    // ★追加: 敵戦車も最大弾数を管理するための変数（プレイヤーと同様）
    [Tooltip("現在の装填されている弾数")]
    private int _currentAmmoCount;
    private float _fireCooldownTimer = 0f;
    // ★追加: スマートエイム用変数
    private Vector3 _smartAimDir = Vector3.zero;
    private float _smartAimTimer = 0f;

    // --- 腰巾着・リーダーシップ用 ---
    private TankStatus _leaderTarget; // 腰巾着が追従する対象

    // --- シンプルモード（パーツドロップ用） ---
    private int _partsDropCount = 0;
    private bool _hasDroppedParts = false;
    private bool _isDropCountOverridden = false;

    // スポーナーなどがドロップ数を強制的に上書きするための関数
    public void SetDropPartsCount(int count)
    {
        _partsDropCount = count;
        _isDropCountOverridden = true;
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _myColliders = GetComponentsInChildren<Collider>();
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

        // NavMeshAgentを取得し、勝手に動かないように設定
        _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (_agent != null)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;
        }

        // ★修正①: ゲーム開始時に最大弾数をセット（プレイヤーと同じ挙動）
        _currentAmmoCount = tankStatus.GetTotalMaxAmmo();

        // 初期ターゲット設定
        DecideNextMoveTarget();

        // スポーナーに上書きされていなければ、EnemyDataのドロップ数をセット
        if (!_isDropCountOverridden && enemyData != null)
        {
            _partsDropCount = enemyData.partsDropCount;
        }
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

        // ============================================
        // ★修正: 暴走モード中の専用思考
        // ============================================
        if (tankStatus.isJammingBerserk)
        {
            ThinkTarget(); // ターゲット（敵）は探し続ける

            // ★修正: 砲塔は常に狙っている敵を睨み続ける
            if (turretTransform != null)
            {
                if (_currentTarget != null && !_currentTarget.IsDead)
                {
                    Vector3 aimDir = (_currentTarget.transform.position - turretTransform.position).normalized;
                    aimDir.y = 0;
                    if (aimDir != Vector3.zero)
                    {
                        // 通常時と同じ旋回速度で滑らかに敵を追従する
                        _independentTurretRotation = Quaternion.RotateTowards(_independentTurretRotation, Quaternion.LookRotation(aimDir), enemyData.turretRotationSpeed * Time.deltaTime);
                    }
                }
                else
                {
                    // 敵を見失った場合は正面を向く
                    _independentTurretRotation = transform.rotation;
                }
            }

            return; // 暴走中は通常の思考（弾避けや地雷設置など）を行わない
        }
        // ============================================

        ThinkTarget();
        ThinkMoveLogic();
        HandleTurretAI();

        if (!_isActionRigid) ThinkMine();

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
        if (_rb == null || _rb.isKinematic) return;
        if (GameManager.Instance != null && (!GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished())) return;

        if (_agent != null && !_agent.isOnNavMesh)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(_rb.position, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                _agent.Warp(hit.position);
            }
        }

        // ★修正: 暴走モード中は、スタン(IsInStun)や硬直(_isActionRigid)を一切無視して強制的に動かす
        if (tankStatus.isJammingBerserk)
        {
            ExecuteBerserkMovement();
            return; // 暴走中ならここで終わり。これより下の「硬直で止める」処理は実行されない
        }

        // --- 以下は通常モード用の処理 ---
        if (_isActionRigid || tankStatus.IsInStun)
        {
            StopMovementImmediate();
            return;
        }

        ExecuteMovement();
    }

    private void LateUpdate()
    {
        if (tankStatus.IsDead || tankStatus.IsInStun) return;
        if (!_isActionRigid) HandleTurretRotation(); // 硬直中は回転しない場合はここにガードを入れる
    }

    // --- AI思考: 移動 ---

    private void ThinkMoveLogic()
    {
        _moveTimer += Time.deltaTime;

        // --- 1. 次の目的地の更新判定 (ニート以外は止まらない) ---
        bool shouldUpdateTarget = false;

        if (enemyData.aiType != EnemyData.AIType.Neat)
        {
            // NavMesh上で目的地までの距離を確認
            float distToDest = (_agent != null && _agent.isOnNavMesh && _agent.hasPath)
                ? _agent.remainingDistance
                : Vector3.Distance(transform.position, _moveTarget);

            // 目的地に近づいたら(2m以内)、または一定時間経過したら即座に更新
            // ★「止まらない」ために、到着する前に次の指示を出す
            if (distToDest < 2.0f || _moveTimer > 5.0f)
            {
                shouldUpdateTarget = true;
            }
        }

        if (shouldUpdateTarget)
        {
            DecideNextMoveTarget();
            _moveTimer = 0f; // タイマーリセット
        }

        // スタック検知（詰まったら強制更新）
        if (_rb.linearVelocity.magnitude < 0.1f && !_isActionRigid && enemyData.aiType != EnemyData.AIType.Neat)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer > 1.0f)
            {
                DecideNextMoveTarget();
                _stuckTimer = 0f;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }

        // --- 2. NavMeshへの指示出し ---
        if (_agent != null && _agent.isOnNavMesh)
        {
            Vector3 finalDestination = _moveTarget; // デフォルトはランダム地点

            switch (enemyData.aiType)
            {
                case EnemyData.AIType.Neat:
                    finalDestination = transform.position; // 動かない
                    break;

                case EnemyData.AIType.Idiot:
                case EnemyData.AIType.Wanderer:
                    // ランダム地点へ直行（DecideNextMoveTargetで更新され続ける）
                    finalDestination = _moveTarget;
                    break;

                case EnemyData.AIType.Aggressive:
                    if (_currentTarget != null)
                    {
                        float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);

                        // 遠ければ追いかける
                        if (dist > 5.0f)
                        {
                            finalDestination = _currentTarget.transform.position;
                        }
                        // 近すぎたら少し下がる
                        else if (dist < 3.0f)
                        {
                            Vector3 awayDir = (transform.position - _currentTarget.transform.position).normalized;
                            finalDestination = transform.position + awayDir * 3.0f;
                        }
                        // ちょうどいい距離なら「その場で停止」ではなく「周りを回る/横にずれる」
                        else
                        {
                            // ターゲットを中心に90度横へずれた位置を目指す（常に動く）
                            Vector3 toTarget = (_currentTarget.transform.position - transform.position).normalized;
                            Vector3 sideDir = Vector3.Cross(toTarget, Vector3.up).normalized;
                            finalDestination = transform.position + sideDir * 3.0f;
                        }
                    }
                    break;

                case EnemyData.AIType.Coward:
                    if (_currentTarget != null)
                    {
                        float distToPlayer = Vector3.Distance(transform.position, _currentTarget.transform.position);

                        if (distToPlayer < 10.0f) // ★修正: 10m以内に近づかれたら逃げる
                        {
                            Vector3 awayDir = (transform.position - _currentTarget.transform.position).normalized;
                            Vector3 randomWobble = new Vector3(Mathf.Sin(Time.time * 2f), 0, Mathf.Cos(Time.time * 2f)) * 0.5f;
                            Vector3 runDir = (awayDir + randomWobble).normalized;

                            Vector3 targetPos = transform.position + runDir * 6.0f;

                            if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out UnityEngine.AI.NavMeshHit navHit, 4.0f, UnityEngine.AI.NavMesh.AllAreas))
                            {
                                finalDestination = navHit.position;
                            }
                            else
                            {
                                Vector3 toCenter = (Vector3.zero - transform.position).normalized;
                                finalDestination = transform.position + toCenter * 3.0f;
                            }
                        }
                        else
                        {
                            // ★修正: 十分に距離が離れている場合は、無駄に逃げずにその周辺をランダムにウロウロする
                            if (Vector3.Distance(transform.position, _moveTarget) < 1.5f || Vector3.Distance(_moveTarget, _currentTarget.transform.position) < 8.0f)
                            {
                                Vector2 randCircle = Random.insideUnitCircle * 4.0f;
                                Vector3 wanderPos = transform.position + new Vector3(randCircle.x, 0, randCircle.y);

                                if (UnityEngine.AI.NavMesh.SamplePosition(wanderPos, out UnityEngine.AI.NavMeshHit navHit, 4.0f, UnityEngine.AI.NavMesh.AllAreas))
                                {
                                    _moveTarget = navHit.position;
                                }
                            }
                            finalDestination = _moveTarget;
                        }
                    }
                    else
                    {
                        finalDestination = _moveTarget;
                    }
                    break;

                case EnemyData.AIType.Sycophant:
                    if (_leaderTarget == null || _leaderTarget.IsDead)
                    {
                        var allies = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
                            .Where(t => t.team == tankStatus.team && t != tankStatus && !t.IsDead)
                            .ToList();
                        if (allies.Count > 0) _leaderTarget = allies[Random.Range(0, allies.Count)];
                    }

                    if (_leaderTarget != null)
                    {
                        // 味方がいる場合、味方の周囲のランダムな位置をウロウロする
                        if (Vector3.Distance(transform.position, _moveTarget) < 1.0f || Vector3.Distance(_moveTarget, _leaderTarget.transform.position) > 4.0f)
                        {
                            Vector2 randCircle = Random.insideUnitCircle * 3.0f;
                            Vector3 targetPos = _leaderTarget.transform.position + new Vector3(randCircle.x, 0, randCircle.y);

                            // ★ここでも安全な座標に補正
                            if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out UnityEngine.AI.NavMeshHit navHit, 3.0f, UnityEngine.AI.NavMesh.AllAreas))
                            {
                                _moveTarget = navHit.position;
                            }
                            else
                            {
                                _moveTarget = _leaderTarget.transform.position;
                            }
                        }
                        finalDestination = _moveTarget;
                    }
                    else
                    {
                        finalDestination = _moveTarget;
                    }
                    break;

                case EnemyData.AIType.Leadership:
                    int allyCount = CountAllies();
                    if (allyCount > 0) // ビビり挙動（逃げ続ける）
                    {
                        if (_currentTarget != null)
                        {
                            Vector3 awayDir = (transform.position - _currentTarget.transform.position).normalized;
                            finalDestination = transform.position + awayDir * 5.0f;
                        }
                    }
                    else // 積極的挙動（攻め続ける）
                    {
                        if (_currentTarget != null) finalDestination = _currentTarget.transform.position;
                    }
                    break;
            }

            // Agentにセット
            _agent.SetDestination(finalDestination);
        }
    }

    [Tooltip("ランダム移動など、次の目的地を決定する")]
    private void DecideNextMoveTarget()
    {
        _moveTimer = 0;

        switch (enemyData.aiType)
        {
            case EnemyData.AIType.Neat:
                _moveTarget = transform.position;
                break;

            case EnemyData.AIType.Idiot:
                _moveTarget = GetRandomStagePoint(); // 完全ランダム
                break;

            case EnemyData.AIType.Wanderer:
            case EnemyData.AIType.Sycophant: // ★修正: 味方がいない腰巾着はWanderer（散歩好き）と同じ処理にする
                _moveTarget = GetFarRandomPoint(); // なるべく遠くのランダムポイントへ移動
                break;

            default:
                _moveTarget = GetRandomStagePoint();
                break;
        }
    }

    // --- 物理移動実行 ---

    [Tooltip("物理的な移動を即座に停止させる")]
    private void StopMovementImmediate()
    {
        // ★修正③: プレイヤー同様、Velocityを0にして確実に止める（めり込み・滑り防止）
        _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
    }

    private void ExecuteMovement()
    {
        if (_isActionRigid || tankStatus.IsInStun)
        {
            StopMovementImmediate();
            return;
        }

        if (_agent == null || !_agent.isOnNavMesh)
        {
            StopMovementImmediate();
            return;
        }

        Vector3 baseDir = _agent.desiredVelocity;

        // ニート以外で目標方向がない場合はとりあえず前進
        if (enemyData.aiType != EnemyData.AIType.Neat && baseDir.magnitude < 0.1f)
        {
            baseDir = transform.forward * tankStatus.GetCurrentMoveSpeed();
        }

        // バカ特有の蛇行運転
        if (enemyData.aiType != EnemyData.AIType.Neat && baseDir.magnitude > 0.1f)
        {
            float wobbleSpeed = (enemyData.aiType == EnemyData.AIType.Idiot) ? 3f : 1.5f;
            float wobbleAmount = (enemyData.aiType == EnemyData.AIType.Idiot) ? 0.8f : 0.3f;
            baseDir += Vector3.Cross(baseDir.normalized, Vector3.up) * Mathf.Sin(Time.time * wobbleSpeed) * wobbleAmount;
        }

        Vector3 finalDir = baseDir.normalized;

        // --- 回避処理 ---
        Vector3 tankAvoid = GetAvoidanceVector("Tank");
        if (tankAvoid != Vector3.zero) finalDir = (finalDir + tankAvoid * 5.0f).normalized;

        Vector3 deadlyAvoid = GetAvoidanceVector("Deadly");
        if (deadlyAvoid != Vector3.zero) finalDir = (finalDir * 0.4f + deadlyAvoid * 3.0f).normalized;

        Vector3 wallAvoid = GetWallAvoidanceVector(5.0f);
        if (wallAvoid != Vector3.zero) finalDir = (finalDir * 0.2f + wallAvoid * 5.0f).normalized;

        if (finalDir.magnitude < 0.1f) finalDir = transform.forward;

        // 壁滑り処理
        int obstacleMask = LayerMask.GetMask("Wall", "Spike");
        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.5f, finalDir, out RaycastHit sphereHit, 1.2f, obstacleMask))
        {
            Vector3 wallNormal = sphereHit.normal; wallNormal.y = 0;
            Vector3 slideDir = Vector3.ProjectOnPlane(finalDir, wallNormal);
            finalDir = (slideDir.magnitude > 0.1f) ? slideDir.normalized : wallNormal.normalized;
        }

        if (finalDir != Vector3.zero) _smoothedMoveDir = Vector3.Lerp(_smoothedMoveDir, finalDir, Time.fixedDeltaTime * 6.0f).normalized;
        if (_smoothedMoveDir == Vector3.zero) _smoothedMoveDir = transform.forward;

        // 実際の移動処理
        if (_smoothedMoveDir.magnitude > 0.1f && enemyData.aiType != EnemyData.AIType.Neat)
        {
            float targetAngle = Mathf.Atan2(_smoothedMoveDir.x, _smoothedMoveDir.z) * Mathf.Rad2Deg;
            float currentY = _rb.rotation.eulerAngles.y;
            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            // 車体が目標方向を向いていない（45度以上ズレている）時は、進まずにその場で旋回だけする
            if (Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle)) > 45.0f)
            {
                _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            }
            else
            {
                // ★修正: ちゃんと正面（transform.forward）に向かって速度を出して進ませる
                Vector3 vel = transform.forward * tankStatus.GetCurrentMoveSpeed();
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
        }
        else
        {
            StopMovementImmediate();
        }

        if (_agent != null && _agent.isOnNavMesh) _agent.nextPosition = _rb.position;
    }

    [Tooltip("暴走モード（ジャミング特攻）中の専用移動処理：NavMeshと壁滑りを組み合わせて絶対にスタックせず敵を追う")]
    private void ExecuteBerserkMovement()
    {
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = false;

            // ターゲットがいれば、常にその位置を目的地に設定
            if (_currentTarget != null && !_currentTarget.IsDead)
            {
                // 計算パンクを防ぐため、目的地が1m以上ズレた時だけ更新する
                if (Vector3.Distance(_agent.destination, _currentTarget.transform.position) > 1.0f)
                {
                    _agent.SetDestination(_currentTarget.transform.position);
                }
            }

            // 基本的な進行方向はNavMeshの「次に曲がるべき角」
            Vector3 desiredDir = _agent.steeringTarget - transform.position;
            desiredDir.y = 0;

            // 経路計算がまだ出ていない時の一瞬の保険（立ち止まり防止）
            if (desiredDir.magnitude < 0.1f && _currentTarget != null && !_currentTarget.IsDead)
            {
                desiredDir = (_currentTarget.transform.position - transform.position);
                desiredDir.y = 0;
            }

            if (desiredDir.magnitude > 0.1f)
            {
                desiredDir.Normalize();

                // ★追加: 壁の角などに引っかからないよう、物理的な「壁滑り・押し出し」を適用する
                Vector3 wallAvoid = GetWallAvoidanceVector(3.0f); // 暴走中は少し短めの距離で検知
                if (wallAvoid != Vector3.zero)
                {
                    desiredDir = (desiredDir * 0.4f + wallAvoid * 5.0f).normalized;
                }

                int obstacleMask = LayerMask.GetMask("Wall", "Spike");
                if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.5f, desiredDir, out RaycastHit sphereHit, 1.2f, obstacleMask))
                {
                    Vector3 wallNormal = sphereHit.normal; wallNormal.y = 0;
                    Vector3 slideDir = Vector3.ProjectOnPlane(desiredDir, wallNormal);
                    desiredDir = (slideDir.magnitude > 0.1f) ? slideDir.normalized : wallNormal.normalized;
                }

                // 滑らかに目標の方向へ車体を回転させる
                float targetAngle = Mathf.Atan2(desiredDir.x, desiredDir.z) * Mathf.Rad2Deg;
                float currentY = _rb.rotation.eulerAngles.y;
                float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime);
                _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

                // 向いた方向へ前進（速度はTankStatus側でアップ済み）
                Vector3 vel = transform.forward * tankStatus.GetCurrentMoveSpeed();
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
            else
            {
                _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            }

            _agent.nextPosition = _rb.position;
        }
    }

    // --- 回避ベクトル計算用のヘルパー関数 ---

    [Tooltip("指定したタイプの危険物からの回避ベクトルを計算する（EnemyDataの設定値を反映）")]
    private Vector3 GetAvoidanceVector(string type)
    {
        // 検知するための最大半径（EnemyDataの設定値の中で一番大きいものを使う）
        float maxSearchRadius = 3.5f; // 戦車避けの基本値
        if (enemyData != null)
        {
            maxSearchRadius = Mathf.Max(maxSearchRadius, enemyData.shellAvoidRadius, enemyData.mineAvoidRadius, enemyData.allyMineAvoidRadius);
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
                // ★弾避け：EnemyData.shellAvoidRadius の範囲内のみ避ける
                if (hit.CompareTag("Shell"))
                {
                    float avoidRad = (enemyData != null) ? enemyData.shellAvoidRadius : 3.0f;
                    if (dist < avoidRad) avoidVec += awayDir * (1.0f - dist / avoidRad);
                }
                // ★地雷避け：EnemyData.mineAvoidRadius / allyMineAvoidRadius を敵味方で使い分ける
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
                    if (enemyData != null)
                    {
                        avoidRad = (mineTeam == tankStatus.team) ? enemyData.allyMineAvoidRadius : enemyData.mineAvoidRadius;
                    }

                    if (dist < avoidRad) avoidVec += awayDir * (1.0f - dist / avoidRad);
                }
            }
            else if (type == "Tank")
            {
                TankStatus otherTank = hit.GetComponentInParent<TankStatus>();
                if (otherTank != null && !otherTank.IsDead)
                {
                    float avoidRad = 3.5f; // 他の戦車との車間距離
                    if (dist < avoidRad) avoidVec += awayDir * (1.0f - dist / avoidRad);
                }
            }
        }
        return avoidVec;
    }

    [Tooltip("周囲の壁を検知して押し出されるベクトルを計算する")]
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

    // --- 地雷設置ロジック ---

    private void ThinkMine()
    {
        if (!enemyData.useMine) return;
        if (tankStatus.ActiveMineCount >= tankStatus.GetData().maxMines) return;
        if (tankStatus.ActiveMineCount >= tankStatus.GetTotalMineLimit()) return;

        // 近くに既存の地雷があれば置かない（密集防止）
        if (Physics.OverlapSphere(transform.position, enemyData.minePlacementSpacing).Any(c => c.CompareTag("Mine"))) return;

        bool shouldPlace = false;
        // ターゲット（プレイヤー等）との距離
        float distToTarget = (_currentTarget != null) ? Vector3.Distance(transform.position, _currentTarget.transform.position) : 999f;

        switch (enemyData.aiType)
        {
            case EnemyData.AIType.Neat:
                shouldPlace = false; // 使用しない
                break;

            case EnemyData.AIType.Idiot:
                // 味方の近く(5m以内)では使用しない、それ以外は低確率(1%)でランダム
                if (CountAlliesNearby(5.0f) == 0 && Random.value < 0.01f) shouldPlace = true;
                break;

            case EnemyData.AIType.Coward:
                // プレイヤーに近づかれる(6m以内)と設置して逃げ道を作る
                if (distToTarget < 6.0f) shouldPlace = true;
                break;

            case EnemyData.AIType.Aggressive:
                // プレイヤーが近く(5m以内)にいると攻撃的に設置
                if (distToTarget < 5.0f) shouldPlace = true;
                break;

            case EnemyData.AIType.Wanderer:
                // 配置できるなら確率(2%)でばら撒く
                if (Random.value < 0.02f) shouldPlace = true;
                break;

            case EnemyData.AIType.Sycophant:
                // 味方がいる時は使用しない（リーダーの邪魔になるため）
                if (_leaderTarget != null)
                {
                    shouldPlace = false;
                }
                else
                {
                    // 味方がいない時は「散歩好き」同様、確率で使用
                    if (Random.value < 0.02f) shouldPlace = true;
                }
                break;

            case EnemyData.AIType.Leadership:
                int allyCount = CountAllies();
                if (allyCount > 0)
                {
                    // 味方がいる＝ビビり挙動（接近されたら置く）
                    if (distToTarget < 6.0f) shouldPlace = true;
                }
                else
                {
                    // 自分のみ＝積極的挙動（攻めで置く）
                    if (distToTarget < 5.0f) shouldPlace = true;
                }
                break;
        }

        if (shouldPlace)
        {
            StartCoroutine(MineRoutine());
        }
    }

    private IEnumerator MineRoutine()
    {
        _isActionRigid = true;
        GameObject prefabToUse = minePrefab != null ? minePrefab : tankStatus.GetMinePrefab();

        if (prefabToUse != null)
        {
            GameObject mineObj = Instantiate(prefabToUse, transform.position, Quaternion.identity);

            // ★修正: 地雷、ロボットボムに加えて、タンクボックスも正しくチームを渡して起動するように変更
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
        yield return new WaitForSeconds(tankStatus.GetData().shotDelay);
        _isActionRigid = false;
    }

    private void HandleTurretAI()
    {
        // ★修正: リターンで終了してしまう前に、必ずターゲット（敵）を探す処理を行う
        ThinkTarget();

        if (turretTransform == null) return;

        // 暴走モード中は砲塔を正面に固定して終了（既存の処理には干渉しない）
        if (tankStatus.isJammingBerserk)
        {
            _independentTurretRotation = transform.rotation;
            return;
        }

        Vector3 targetDir = Vector3.forward;
        if (_currentTarget != null)
        {
            targetDir = (_currentTarget.transform.position - turretTransform.position).normalized;

            if (enemyData.useSmartRicochet)
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

        bool canShoot = CheckShootTrajectory();

        float offsetAngle = 0f;
        if (!canShoot && _smartAimDir == Vector3.zero)
        {
            _turretNoiseTime += Time.deltaTime * 0.8f;
            float noise = Mathf.PerlinNoise(_turretNoiseTime, 0f) * 2.0f - 1.0f;
            offsetAngle = noise * (enemyData.turretSearchAngle + 30f);
        }

        if (targetDir != Vector3.zero)
        {
            Quaternion baseRot = Quaternion.LookRotation(targetDir);
            Quaternion finalRot = baseRot * Quaternion.Euler(0, offsetAngle, 0);

            _independentTurretRotation = Quaternion.RotateTowards(_independentTurretRotation, finalRot, enemyData.turretRotationSpeed * Time.deltaTime);
        }
    }

    private void HandleTurretRotation()
    {
        if (turretTransform != null)
        {
            turretTransform.rotation = _independentTurretRotation;
        }

        // ★追加: 暴走中は弾を撃たないようにここで処理を止める
        if (tankStatus.isJammingBerserk) return;

        // 射撃試行
        if (CheckShootTrajectory())
        {
            TryFire();
        }
    }

    [Tooltip("射撃の試行（射線と弾数、壁めり込みの確認）")]
    public void TryFire()
    {
        if (tankStatus.IsInStun || _isActionRigid || _fireCooldownTimer > 0 || _currentAmmoCount <= 0) return;

        int wallLayerMask = LayerMask.GetMask("Wall");
        Vector3 muzzlePos = firePoint.position;
        Vector3 turretCenter = turretTransform != null ? turretTransform.position : transform.position;

        // ★壁撃ち・透視バグ防止の最終防衛線:
        // 発射口が壁にめり込んでいる場合は、AIが撃ちたがっても絶対に引き金を引かせない
        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;
        if (Physics.CheckSphere(muzzlePos, checkRadius, wallLayerMask)) return;
        if (Physics.Linecast(turretCenter, muzzlePos, wallLayerMask)) return;

        StartCoroutine(FireRoutine());
    }

    [Tooltip("実際の射撃処理と硬直時間の管理")]
    private IEnumerator FireRoutine()
    {
        _isActionRigid = true; // 硬直開始
        _currentAmmoCount--;   // 撃ったので弾を1つ消費する

        if (tankStatus.GetShellPrefab() != null && firePoint != null)
        {
            if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(firePoint);
            GameObject shellObj = Instantiate(tankStatus.GetShellPrefab(), firePoint.position, firePoint.rotation);
            EffectManager.Instance.ShotSound();

            // ★修正: ここにあった Physics.IgnoreCollision（すり抜け処理）を完全に削除しました。
            // これにより、跳弾してきた自分の弾が自分自身に当たって自爆するようになります（プレイヤーと同じ条件）。

            ShellController shell = shellObj.GetComponent<ShellController>();
            if (shell != null) shell.Launch(gameObject, 0);
        }

        // 撃ったらプレイヤーと同じルールでリロード処理を開始する
        StartCoroutine(ReloadAmmoRoutine());

        // 硬直待機（ShotDelay）
        yield return new WaitForSeconds(tankStatus.GetData().shotDelay);

        // 元々の「敵の攻撃クールタイム」をここでセットし、連射を防ぐ
        _fireCooldownTimer = enemyData.fireCooldown;

        _isActionRigid = false; // 硬直解除
    }

    [Tooltip("弾を1発ずつ回復する処理（プレイヤーと同じ挙動）")]
    private IEnumerator ReloadAmmoRoutine()
    {
        // ★修正: 敵のfireCooldownではなく、プレイヤーと同じ「弾の回復時間(ammoCooldown)」を参照する
        yield return new WaitForSeconds(tankStatus.GetData().ammoCooldown);

        int totalMax = tankStatus.GetTotalMaxAmmo();
        if (_currentAmmoCount < totalMax)
        {
            _currentAmmoCount++;
        }
    }

    // --- ユーティリティ・チェック関数 ---

    private void ThinkTarget()
    {
        // ターゲットが生きていれば、戦略に応じて維持判定
        if (_currentTarget != null && !_currentTarget.IsDead)
        {
            // ★追加: 暴走中（ジャミング特攻中）は、一度決めたターゲットを死ぬまで絶対に！変えない
            if (tankStatus.isJammingBerserk)
            {
                return;
            }

            if (enemyData.targetStrategy == EnemyData.TargetStrategy.Persistent)
            {
                return; // 執着：死ぬまで変えない
            }
            else if (enemyData.targetStrategy == EnemyData.TargetStrategy.Capricious)
            {
                // 気まぐれ：時間が来るまで変えない（近距離優先の再検索をしない）
                if (Time.time < _nextTargetUpdateTime) return;
            }
        }

        // 新規検索（距離優先）
        var targets = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            // ★修正: ターゲットに選ぶ条件に「チームが自分と違うこと」を厳密に指定
            // スポーンボックスで生まれた戦車が、親（同じチーム）を誤認しないようにする
            .Where(t => t != null && t.team != tankStatus.team && !t.IsDead)
            .OrderBy(t => Vector3.Distance(transform.position, t.transform.position))
            .ToList();

        _currentTarget = targets.FirstOrDefault();

        // 気まぐれの場合、次回の更新時間をセット（3秒 + ランダム0~3秒）
        if (enemyData.targetStrategy == EnemyData.TargetStrategy.Capricious)
        {
            _nextTargetUpdateTime = Time.time + 3.0f + Random.Range(0f, 3.0f);
        }
    }

    [Tooltip("現在向いている方向に撃った場合、敵に当たるか（射線が通っているか）を判定する")]
    private bool CheckShootTrajectory()
    {
        if (_currentTarget == null || firePoint == null) return false;

        Vector3 startPos = firePoint.position;
        Vector3 dir = firePoint.forward;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        // 砲塔の超近距離（目の前）での誤爆・誤射防止チェック
        // 砲身の先端から半径0.8mの範囲をスキャンする
        Collider[] closeHits = Physics.OverlapSphere(startPos, 2.0f);
        foreach (var hit in closeHits)
        {
            // 自分自身は無視
            if (hit.transform.IsChildOf(transform)) continue;

            // ① 目の前に地雷がある場合は絶対に撃たない（撃つと自爆するため）
            if (hit.CompareTag("Mine"))
            {
                return false;
            }

            // ② 目の前に味方がいる場合は撃たない（EnemyDataで味方を意識する設定の時のみ）
            if (enemyData != null && enemyData.isTeamAware)
            {
                TankStatus closeTank = hit.GetComponentInParent<TankStatus>();
                if (closeTank != null && closeTank.team == tankStatus.team && !closeTank.IsDead)
                {
                    return false;
                }
            }
        }

        // --- 以下、既存の射線計算処理 ---

        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (enemyData != null && !enemyData.considerReflection) maxBounces = 0;

        // ★スマートエイムONで、正解の角度が見つかっている場合
        if (enemyData != null && enemyData.useSmartRicochet && _smartAimDir != Vector3.zero)
        {
            float angleDiff = Vector3.Angle(dir, _smartAimDir);
            if (angleDiff <= enemyData.shotAllowAngle)
            {
                // 撃つ瞬間にターゲットが動いていないか最終確認
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

        // ★スマートエイムOFF時、または直接狙う時
        return SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0);
    }

    private Vector3 GetRandomStagePoint()
    {
        return new Vector3(
            Random.Range(-STAGE_LIMIT, STAGE_LIMIT),
            0,
            Random.Range(-STAGE_LIMIT, STAGE_LIMIT)
        );
    }

    private Vector3 GetFarRandomPoint()
    {
        // 現在地から遠い場所を探す
        for (int i = 0; i < 10; i++)
        {
            Vector3 p = GetRandomStagePoint();
            if (Vector3.Distance(transform.position, p) > 10.0f) return p;
        }
        return -transform.position; // 見つからなければ反対側へ
    }

    private int CountAllies() => FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Count(t => t.team == tankStatus.team && t != tankStatus && !t.IsDead);
    private int CountAlliesNearby(float radius) => Physics.OverlapSphere(transform.position, radius).Select(c => c.GetComponentInParent<TankStatus>()).Count(t => t != null && t.team == tankStatus.team && t != tankStatus && !t.IsDead);

    // --- 跳弾ルート探索（スマートエイム） ---

    [Tooltip("プレイヤーの方向を中心に、左右に扇状に広がりながら跳弾ルートをスキャンする")]
    private Vector3 FindSmartRicochetDirection()
    {
        if (firePoint == null || _currentTarget == null) return Vector3.zero;

        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (maxBounces <= 0 || enemyData == null || !enemyData.considerReflection) return Vector3.zero;

        Vector3 startPos = firePoint.position;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike") & ~LayerMask.GetMask("Mine") & ~LayerMask.GetMask("Ignore Raycast");

        // ★修正: プレイヤーがいる方向を基準（0度）にする
        Vector3 baseDir = (_currentTarget.transform.position - startPos).normalized;
        baseDir.y = 0;
        if (baseDir == Vector3.zero) baseDir = transform.forward;

        // プレイヤーの方向から、左右に3度ずつ広がりながら真後ろ(180度)まで徹底的に探す
        // これにより「一番少ないバウンド数で当たる、最も確実なルート」を最優先で見つけ出します
        for (int angle = 0; angle <= 180; angle += 3)
        {
            Vector3 rightDir = Quaternion.Euler(0, angle, 0) * baseDir;
            if (SimulateRaycastTrajectory(startPos, rightDir, maxBounces, layerMask, 0))
            {
                return rightDir;
            }

            if (angle != 0 && angle != 180)
            {
                Vector3 leftDir = Quaternion.Euler(0, -angle, 0) * baseDir;
                if (SimulateRaycastTrajectory(startPos, leftDir, maxBounces, layerMask, 0))
                {
                    return leftDir;
                }
            }
        }
        return Vector3.zero;
    }


    [Tooltip("球(SphereCast)を飛ばして跳弾をシミュレーションする")]
    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce)
    {
        // 高バウンド弾にも対応できるよう、再帰制限を深めに設定
        if (currentBounce > 15) return false;

        // 弾が地面に向かって飛んで床に当たらないよう、Y軸を完全に水平に固定する
        dir.y = 0;
        dir.Normalize();

        float checkRadius = (enemyData != null) ? enemyData.raycastRadius : 0.25f;

        RaycastHit[] hits = Physics.SphereCastAll(startPos, checkRadius, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;

            // ★修正: 視界の重なり（distance==0）は無視する。
            // 透視バグは「TryFire」の壁チェックで完全に防いでいるため、ここでは純粋なルート計算のみを行う
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
                // 敵ならTrue（射撃実行）、味方ならFalse（射撃中止）
                return hitTank.team != tankStatus.team;
            }
        }
        return false;
    }

    [Tooltip("死亡時にパーツをばらまく（倒したプレイヤーに自動で吸い込まれるが、FF時はその場に落ちる）")]
    private void DropParts()
    {
        if (_partsDropCount <= 0 || GameManager.Instance == null) return;

        GameObject prefab = GameManager.Instance.GetPartsItemPrefab();
        if (prefab == null) return;

        var survivingPlayers = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            .Where(t => t.team == TeamType.Blue && !t.IsDead)
            .ToList();

        TankStatus targetPlayer = tankStatus.LastAttacker;

        // ★追加: フレンドリーファイア（敵同士の同士討ち）の判定
        bool isFriendlyFire = false;
        if (targetPlayer != null && targetPlayer.team != TeamType.Blue)
        {
            isFriendlyFire = true;
        }

        bool isBoss = (enemyData != null && enemyData.isBossDrop);

        if (isFriendlyFire)
        {
            // 【FF用】誰にも吸い込ませず、その場に散らばるだけにする（targetPlayerをnullにして渡す）
            SpawnAndMagnetParts(prefab, _partsDropCount, null);
        }
        else if (isBoss)
        {
            // 【ボス用】生存しているプレイヤー全員に配る
            int survivingCount = survivingPlayers.Count;
            if (survivingCount == 0) return;
            int partsPerPlayer = Mathf.Max(0, _partsDropCount + 1 - survivingCount);

            foreach (var player in survivingPlayers)
            {
                SpawnAndMagnetParts(prefab, partsPerPlayer, player);
            }
        }
        else
        {
            // 【通常用】ラストアタックを行ったプレイヤーに配る
            if (targetPlayer == null || targetPlayer.IsDead || targetPlayer.team != TeamType.Blue)
            {
                targetPlayer = survivingPlayers.OrderBy(t => Vector3.Distance(transform.position, t.transform.position)).FirstOrDefault();
            }

            SpawnAndMagnetParts(prefab, _partsDropCount, targetPlayer);
        }
    }

    // パーツを生成し、指定したターゲットへ向けてマグネット化するヘルパー関数
    private void SpawnAndMagnetParts(GameObject prefab, int count, TankStatus targetPlayer)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPos = transform.position + Vector3.up * 1.0f;
            GameObject partObj = Instantiate(prefab, spawnPos, Quaternion.identity);

            Rigidbody rb = partObj.GetComponent<Rigidbody>();
            if (rb == null) rb = partObj.AddComponent<Rigidbody>();

            Vector3 force = Vector3.up * 2.5f + Random.insideUnitSphere * 1.5f;
            rb.AddForce(force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 50f, ForceMode.Impulse);

            // アイテムボックスではなく敵からのドロップなので、必ず自動獲得（マグネット化）させる
            if (targetPlayer != null)
            {
                PartsItemController pic = partObj.GetComponent<PartsItemController>();
                if (pic != null) pic.StartMagneticEffect(targetPlayer);
            }
        }
    }
}