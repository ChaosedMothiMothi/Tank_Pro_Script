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
    private bool _isReloading = false;
    private bool _isActionRigid = false; // 硬直フラグ
    private float _fireCooldownTimer = 0f;

    // --- 腰巾着・リーダーシップ用 ---
    private TankStatus _leaderTarget; // 腰巾着が追従する対象

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

        // ★追加: NavMeshAgentを取得し、勝手に動かないように設定
        _agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (_agent != null)
        {
            _agent.updatePosition = false; // 物理挙動と喧嘩しないようにOFF
            _agent.updateRotation = false; // 物理挙動と喧嘩しないようにOFF
            _agent.updateUpAxis = false;
        }

        // 初期ターゲット設定
        DecideNextMoveTarget();
    }

    private void Update()
    {

        // ★修正: ゲーム終了時は何もしない
        if (tankStatus.IsDead || (GameManager.Instance != null && GameManager.Instance.IsGameFinished()))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_agent != null && _agent.enabled) _agent.isStopped = true; // NavMeshも止める
            return;
        }

        if (tankStatus.IsDead || (GameManager.Instance != null && GameManager.Instance.IsGameFinished()))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            return;
        }

        if (_fireCooldownTimer > 0) _fireCooldownTimer -= Time.deltaTime;

        // 硬直中も思考（ターゲット選定・砲塔制御）は続ける
        ThinkTarget();

        // 性格ごとの移動ロジック更新
        ThinkMoveLogic();

        // 砲塔制御
        HandleTurretAI();

        // 地雷設置（硬直中は不可）
        if (!_isActionRigid)
        {
            ThinkMine();
        }

        // デバッグ表示
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
                        // 常に逃げ続ける
                        Vector3 awayDir = (transform.position - _currentTarget.transform.position).normalized;
                        Vector3 toCenter = (Vector3.zero - transform.position).normalized;

                        float distFromCenter = transform.position.magnitude;
                        float centerBias = Mathf.Clamp01((distFromCenter - 10.0f) / 5.0f);

                        Vector3 runDir = Vector3.Slerp(awayDir, toCenter, centerBias).normalized;
                        finalDestination = transform.position + runDir * 5.0f;
                    }
                    break;

                case EnemyData.AIType.Sycophant:
                    if (_leaderTarget == null || _leaderTarget.IsDead)
                    {
                        var allies = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
                            .Where(t => t.team == tankStatus.team && t != tankStatus && !t.IsDead)
                            .OrderBy(t => Vector3.Distance(transform.position, t.transform.position))
                            .ToList();
                        if (allies.Count > 0) _leaderTarget = allies[0];
                    }

                    if (_leaderTarget != null)
                    {
                        // リーダーの後ろで止まらず、常に少し揺らぐように位置調整
                        // (リーダーが動けばついていくので自然と動くが、止まっている時も少し動かす)
                        Vector3 basePos = _leaderTarget.transform.position - _leaderTarget.transform.forward * 3.0f;
                        // 少しランダムにずらす
                        finalDestination = basePos + new Vector3(Mathf.Sin(Time.time), 0, Mathf.Cos(Time.time)) * 1.0f;
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
                _moveTarget = GetFarRandomPoint(); // 遠くへ移動
                break;

            // 他のタイプは毎フレーム計算するのでターゲット座標の更新は必須ではないが、
            // スタック回避のためにランダム点を入れておく
            default:
                _moveTarget = GetRandomStagePoint();
                break;
        }
    }

    // --- 各性格の移動ロジック ---

    private Vector3 LogicCoward()
    {
        if (_currentTarget == null) return (_moveTarget - transform.position).normalized;

        float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);

        // プレイヤーから逃げるベクトル
        Vector3 runAwayDir = (transform.position - _currentTarget.transform.position).normalized;

        // 角（壁）に追いやられないように、ステージ中央へ向かうベクトルを混ぜる
        Vector3 toCenter = (Vector3.zero - transform.position).normalized;

        // 壁際なら中央への意識を強くする
        float distFromCenter = transform.position.magnitude;
        float centerBias = Mathf.Clamp01((distFromCenter - 10.0f) / 4.0f); // 10m超えたら中央へ戻りたがる

        return Vector3.Slerp(runAwayDir, toCenter, centerBias).normalized;
    }

    private Vector3 LogicAggressive()
    {
        if (_currentTarget == null) return (_moveTarget - transform.position).normalized;

        Vector3 toTarget = (_currentTarget.transform.position - transform.position).normalized;
        float dist = Vector3.Distance(transform.position, _currentTarget.transform.position);

        // ある程度の距離（5m）まで詰める
        if (dist > 5.0f) return toTarget;

        // 近すぎたら少し離れる、または周りを回る
        if (dist < 3.0f) return -toTarget;

        return Vector3.Cross(toTarget, Vector3.up).normalized; // 旋回
    }

    private Vector3 LogicSycophant()
    {
        // 味方を探す
        if (_leaderTarget == null || _leaderTarget.IsDead)
        {
            var allies = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
                .Where(t => t.team == tankStatus.team && t != tankStatus && !t.IsDead)
                .OrderBy(t => Vector3.Distance(transform.position, t.transform.position))
                .ToList();

            if (allies.Count > 0) _leaderTarget = allies[0];
            else _leaderTarget = null;
        }

        // 味方がいない場合は散歩好き（Wanderer）の挙動
        if (_leaderTarget == null)
        {
            return (_moveTarget - transform.position).normalized;
        }

        // 味方の付近（3m後ろなど）をうろつく
        Vector3 targetPos = _leaderTarget.transform.position - _leaderTarget.transform.forward * 3.0f;
        if (Vector3.Distance(transform.position, targetPos) < 1.0f) return Vector3.zero; // 位置についたら停止

        return (targetPos - transform.position).normalized;
    }


    // --- 物理移動実行 ---

    private void StopMovementImmediate()
    {
        Vector3 vel = _rb.linearVelocity;
        Vector3 horizontalVel = new Vector3(vel.x, 0, vel.z);
        _rb.AddForce(-horizontalVel * 10.0f, ForceMode.Acceleration);
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

        Vector3 desiredVel = _agent.desiredVelocity;

        // 危険回避（弾など）
        Vector3 dangerDir = CalculateDangerAvoidance();
        if (dangerDir != Vector3.zero)
        {
            desiredVel = dangerDir.normalized * tankStatus.GetCurrentMoveSpeed();
        }

        // ★追加: 物理的な壁接触防止（ブレーキ）
        // 目の前 0.6m 以内に壁がある場合、前進しようとする力を止める
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, 0.8f, LayerMask.GetMask("Wall")))
        {
            // 壁に向かっているなら速度を殺す
            // (壁から離れる動きは許可する)
            if (Vector3.Dot(desiredVel, transform.forward) > 0)
            {
                desiredVel = Vector3.zero;
                _rb.linearVelocity = Vector3.zero; // 完全に停止させる
            }
        }

        // --- 移動処理 ---
        if (desiredVel.magnitude > 0.1f)
        {
            Vector3 moveDir = desiredVel.normalized;

            // 回転
            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float currentY = transform.eulerAngles.y;
            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, tankStatus.GetData().rotationSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            // 前進（壁ブレーキがかかっていない場合のみ）
            if (desiredVel.magnitude > 0.01f && Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle)) < 45.0f)
            {
                float speed = tankStatus.GetCurrentMoveSpeed();
                Vector3 targetForce = transform.forward * speed;
                Vector3 diff = targetForce - _rb.linearVelocity;
                diff.y = 0;
                _rb.AddForce(diff * 20f, ForceMode.Acceleration);
            }
        }
        else
        {
            StopMovementImmediate();
        }

        _agent.nextPosition = _rb.position;
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

            // ★修正: 通常地雷とロボットボムの両対応
            MineController mineCtrl = mineObj.GetComponent<MineController>();
            if (mineCtrl != null)
            {
                mineCtrl.Init(tankStatus, tankStatus.GetMineData());
                tankStatus.OnMinePlaced();
            }
            else
            {
                RobotBombController robotBomb = mineObj.GetComponent<RobotBombController>();
                if (robotBomb != null)
                {
                    robotBomb.Init(tankStatus, tankStatus.GetMineData());
                    tankStatus.OnMinePlaced();
                }
            }
        }

        yield return new WaitForSeconds(tankStatus.GetData().shotDelay);
        _isActionRigid = false;
    }

    // --- 砲塔・射撃ロジック ---

    private void HandleTurretAI()
    {
        // ターゲット選定
        ThinkTarget();

        if (turretTransform == null) return;

        Vector3 targetDir = Vector3.forward;
        if (_currentTarget != null)
        {
            targetDir = (_currentTarget.transform.position - turretTransform.position).normalized;
        }
        else
        {
            targetDir = transform.forward; // ターゲットがいなければ正面
        }
        targetDir.y = 0;

        // 射撃可否チェック（壁判定含む）
        bool canShoot = CheckShootTrajectory();

        // 索敵首振り（射線が通っていない時）
        float offsetAngle = 0f;
        if (!canShoot)
        {
            _turretNoiseTime += Time.deltaTime * 0.5f;
            float noise = Mathf.PerlinNoise(_turretNoiseTime, 0f) * 2.0f - 1.0f;
            offsetAngle = noise * enemyData.turretSearchAngle;
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

        // 射撃試行（LateUpdateで実行）
        if (CheckShootTrajectory())
        {
            TryFire();
        }
    }

    public void TryFire()
    {
        if (_isReloading || tankStatus.IsInStun || _isActionRigid || _fireCooldownTimer > 0) return;

        // 壁埋まりチェック
        int wallLayerMask = LayerMask.GetMask("Wall");
        Vector3 muzzlePos = firePoint.position;
        Vector3 turretCenter = turretTransform != null ? turretTransform.position : transform.position;

        // 1. 発射点が壁の中か
        if (Physics.CheckSphere(muzzlePos, 0.2f, wallLayerMask)) return;

        // 2. 砲塔中心から発射点へのRay（壁貫通防止）
        if (Physics.Linecast(turretCenter, muzzlePos, wallLayerMask)) return;

        StartCoroutine(FireRoutine());
    }

    private IEnumerator FireRoutine()
    {
        _isReloading = true;
        _isActionRigid = true; // 硬直開始

        if (tankStatus.GetShellPrefab() != null && firePoint != null)
        {
            if (EffectManager.Instance != null) EffectManager.Instance.PlayMuzzleFlash(firePoint);
            GameObject shellObj = Instantiate(tankStatus.GetShellPrefab(), firePoint.position, firePoint.rotation);
            EffectManager.Instance.ShotSound();

            Collider shellCol = shellObj.GetComponent<Collider>();
            if (shellCol != null && _myColliders != null)
            {
                foreach (var c in _myColliders) if (c != null) Physics.IgnoreCollision(shellCol, c);
            }

            ShellController shell = shellObj.GetComponent<ShellController>();
            if (shell != null) shell.Launch(gameObject, 0);
        }

        // 硬直待機（ShotDelay）
        yield return new WaitForSeconds(tankStatus.GetData().shotDelay);

        // ★修正: 硬直が明けてから、次の発射までのクールダウンをセットする
        _fireCooldownTimer = enemyData.fireCooldown;

        _isActionRigid = false; // 硬直解除
        _isReloading = false;
    }

    // --- ユーティリティ・チェック関数 ---

    private void ThinkTarget()
    {
        // ターゲットが生きていれば、戦略に応じて維持判定
        if (_currentTarget != null && !_currentTarget.IsDead)
        {
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
            .Where(t => t.team != tankStatus.team && !t.IsDead)
            .OrderBy(t => Vector3.Distance(transform.position, t.transform.position))
            .ToList();

        _currentTarget = targets.FirstOrDefault();

        // 気まぐれの場合、次回の更新時間をセット（3秒 + ランダム0~3秒）
        if (enemyData.targetStrategy == EnemyData.TargetStrategy.Capricious)
        {
            _nextTargetUpdateTime = Time.time + 3.0f + Random.Range(0f, 3.0f);
        }
    }

    // EnemyTankController.cs 内の CheckShootTrajectory を差し替え

    private bool CheckShootTrajectory()
    {
        if (_currentTarget == null || firePoint == null) return false;

        Vector3 startPos = firePoint.position;
        Vector3 dir = firePoint.forward;
        float dist = 100f;
        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (!enemyData.considerReflection) maxBounces = 0;

        int layerMask = Physics.DefaultRaycastLayers
            & ~LayerMask.GetMask("Spike")
            & ~LayerMask.GetMask("Mine")
            & ~LayerMask.GetMask("Ignore Raycast");

        // --- 1. 超近距離（自爆防止） ---
        if (enemyData.isTeamAware)
        {
            Collider[] closeHits = Physics.OverlapSphere(startPos, 0.3f, layerMask); // 半径を0.5->1.0に拡大し安全度UP
            foreach (var hit in closeHits)
            {
                if (hit.transform.IsChildOf(transform)) continue;
                TankStatus closeTank = hit.GetComponentInParent<TankStatus>();
                if (closeTank != null && closeTank.team == tankStatus.team) return false;
            }
        }

        // --- 2. 射線チェック（細いRayで敵を探す） ---
        RaycastHit[] hits = Physics.SphereCastAll(startPos, 0.3f, dir, dist, layerMask); // 少しだけ太く(0.1->0.15)
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;

            // 壁反射
            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                if (maxBounces > 0)
                {
                    Vector3 reflectDir = Vector3.Reflect(dir, hit.normal);
                    // 反射先チェック（ここは簡易的に細いRayのまま）
                    if (Physics.SphereCast(hit.point + hit.normal * 0.1f, 0.2f, reflectDir, out RaycastHit hit2, dist, layerMask))
                    {
                        TankStatus target2 = hit2.collider.GetComponentInParent<TankStatus>();
                        if (target2 != null && target2.team != tankStatus.team)
                        {
                            // 反射で敵を狙えるが、反射前の経路に味方がいないかチェックが必要
                            if (enemyData.isTeamAware && IsAllyInPath(startPos, dir, hit.distance)) return false;
                            return true;
                        }
                    }
                }
                return false; // 壁で射線切れ
            }

            // 戦車ヒット
            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null)
            {
                if (hitTank.team != tankStatus.team)
                {
                    // ★敵発見！ -> ここで最終安全確認
                    // 「敵までの距離」の間に、もっと太い判定で味方がいないか調べる
                    if (enemyData.isTeamAware)
                    {
                        if (IsAllyInPath(startPos, dir, hit.distance))
                        {
                            return false; // 射線上に味方が（太い判定で）被っているので撃たない
                        }
                    }
                    return true; // 射線クリア、発射！
                }
                else
                {
                    // 味方ヒット（細いRayですら味方に当たった）
                    if (enemyData.isTeamAware) return false;
                }
            }
        }

        return false;
    }

    // ★追加: 指定した距離までの経路に味方がいないか「太いRay」で確認する
    private bool IsAllyInPath(Vector3 start, Vector3 dir, float distance)
    {
        // 戦車の幅くらい（半径0.5m）の太さでチェックする
        float safetyRadius = 0.5f;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Ignore Raycast");

        RaycastHit[] hits = Physics.SphereCastAll(start, safetyRadius, dir, distance, layerMask);

        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;

            TankStatus target = hit.collider.GetComponentInParent<TankStatus>();
            // 味方がいたらアウト
            if (target != null && target.team == tankStatus.team)
            {
                return true;
            }
        }
        return false;
    }

    private bool CheckWallInFront()
    {
        // 進行方向の壁チェック
        return Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, 1.5f, LayerMask.GetMask("Wall"));
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

    // 危険物（弾・地雷）からの回避ベクトルを計算
    // 危険がなければ Vector3.zero を返す
    private Vector3 CalculateDangerAvoidance()
    {
        // 検索半径は最大のものを使用
        float searchRadius = Mathf.Max(enemyData.shellAvoidRadius, enemyData.mineAvoidRadius, enemyData.allyMineAvoidRadius);
        // 少し余裕を持たせる
        Collider[] hits = Physics.OverlapSphere(transform.position, searchRadius + 1.0f);

        Vector3 totalAvoidVec = Vector3.zero;
        int dangerCount = 0;

        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;
            if (hit.transform.IsChildOf(transform)) continue;

            Vector3 toObj = hit.transform.position - transform.position;
            float dist = toObj.magnitude;
            Vector3 awayDir = -toObj.normalized; // 物体から離れる方向

            // --- 弾回避 ---
            if (hit.CompareTag("Shell"))
            {
                ShellController s = hit.GetComponent<ShellController>();
                // 自分の弾以外、かつ範囲内
                if (s != null && s.Owner != gameObject && dist < enemyData.shellAvoidRadius)
                {
                    // 距離が近いほど強く避ける（重み付け）
                    float weight = 1.0f - (dist / enemyData.shellAvoidRadius);
                    totalAvoidVec += awayDir * weight * 3.0f; // 弾は非常に危険
                    dangerCount++;
                }
            }
            // --- 地雷回避 ---
            else if (hit.CompareTag("Mine"))
            {
                // 地雷情報を取得
                TeamType mineTeam = TeamType.Neutral;

                // ※コンポーネント取得の負荷軽減のため、インターフェース化などが理想だが現状の構造で対応
                var mineCtrl = hit.GetComponent<MineController>();
                if (mineCtrl != null) mineTeam = mineCtrl.GetTeam();
                else
                {
                    var robot = hit.GetComponent<RobotBombController>();
                    if (robot != null) mineTeam = robot.GetTeam();
                }

                // 回避半径の決定
                bool isAlly = (mineTeam == tankStatus.team);
                float avoidRadius = isAlly ? enemyData.allyMineAvoidRadius : enemyData.mineAvoidRadius;

                // 範囲内なら避ける
                if (avoidRadius > 0 && dist < avoidRadius)
                {
                    float weight = 1.0f - (dist / avoidRadius);
                    totalAvoidVec += awayDir * weight * 2.0f; // 地雷も強く避ける
                    dangerCount++;
                }
            }
        }

        if (dangerCount > 0)
        {
            return totalAvoidVec.normalized;
        }
        return Vector3.zero;
    }

    // 壁回避ベクトルを計算
    private Vector3 CalculateWallAvoidance(Vector3 currentDir)
    {
        // 移動していなければ正面、移動していればその方向を基準
        Vector3 baseDir = currentDir.sqrMagnitude > 0.01f ? currentDir : transform.forward;

        float rayDist = 2.5f; // 検知距離（車体サイズに合わせて調整）
        int wallMask = LayerMask.GetMask("Wall");

        Vector3 avoidance = Vector3.zero;
        bool hitWall = false;

        // 3本のヒゲ（Ray）を出す
        // 1. 正面
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, baseDir, out RaycastHit hitFront, rayDist, wallMask))
        {
            avoidance += hitFront.normal * 2.0f; // 法線方向に強く押し出す
            hitWall = true;
        }

        // 2. 左斜め30度
        Vector3 leftDir = Quaternion.Euler(0, -30, 0) * baseDir;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, leftDir, out RaycastHit hitLeft, rayDist * 0.8f, wallMask))
        {
            avoidance += hitLeft.normal;
            hitWall = true;
        }

        // 3. 右斜め30度
        Vector3 rightDir = Quaternion.Euler(0, 30, 0) * baseDir;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, rightDir, out RaycastHit hitRight, rayDist * 0.8f, wallMask))
        {
            avoidance += hitRight.normal;
            hitWall = true;
        }

        if (hitWall)
        {
            // 壁から離れるベクトルを正規化して返す
            return avoidance.normalized;
        }

        return Vector3.zero;
    }

    private int CountAllies() => FindObjectsByType<TankStatus>(FindObjectsSortMode.None).Count(t => t.team == tankStatus.team && t != tankStatus && !t.IsDead);
    private int CountAlliesNearby(float radius) => Physics.OverlapSphere(transform.position, radius).Select(c => c.GetComponentInParent<TankStatus>()).Count(t => t != null && t.team == tankStatus.team && t != tankStatus && !t.IsDead);
}