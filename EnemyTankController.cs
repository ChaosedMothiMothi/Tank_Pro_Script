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
    }

    private void Update()
    {
        // ゲーム終了時は何もしない
        if (tankStatus.IsDead || (GameManager.Instance != null && GameManager.Instance.IsGameFinished()))
        {
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            return;
        }

        // 硬直中も思考（ターゲット選定・砲塔制御）は続ける
        ThinkTarget();
        ThinkMoveLogic();
        HandleTurretAI();

        // 地雷設置（硬直中は不可）
        if (!_isActionRigid)
        {
            ThinkMine();
        }

        if (_fireCooldownTimer > 0) _fireCooldownTimer -= Time.deltaTime;

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
                        // 敵から逃げる方向
                        Vector3 awayDir = (transform.position - _currentTarget.transform.position).normalized;
                        Vector3 randomWobble = new Vector3(Mathf.Sin(Time.time * 2f), 0, Mathf.Cos(Time.time * 2f)) * 0.5f;
                        Vector3 runDir = (awayDir + randomWobble).normalized;

                        Vector3 targetPos = transform.position + runDir * 6.0f;

                        // ★修正: 逃げ先が壁の中や場外にならないよう、NavMesh上の安全な座標に補正する
                        if (UnityEngine.AI.NavMesh.SamplePosition(targetPos, out UnityEngine.AI.NavMeshHit navHit, 4.0f, UnityEngine.AI.NavMesh.AllAreas))
                        {
                            finalDestination = navHit.position;
                        }
                        else
                        {
                            // 安全な場所が見つからなければ、とりあえず中央寄りに逃げる
                            Vector3 toCenter = (Vector3.zero - transform.position).normalized;
                            finalDestination = transform.position + toCenter * 3.0f;
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

    [Tooltip("AIの思考に基づく物理的な移動処理（滑らかな移動と障害物回避）")]
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

        // 1. 危険回避（弾・地雷など）を最優先で上書き
        Vector3 dangerDir = CalculateDangerAvoidance();
        if (dangerDir != Vector3.zero)
        {
            desiredVel = dangerDir.normalized * tankStatus.GetCurrentMoveSpeed();
        }

        // 2. ★修正: 壁やトゲへのめり込みを完全に防ぐ「球体（SphereCast）スライダー」
        int obstacleMask = LayerMask.GetMask("Wall", "Spike");
        Vector3 checkDir = desiredVel.magnitude > 0.1f ? desiredVel.normalized : transform.forward;

        // 戦車とほぼ同じ太さ（半径0.5m）の球を進行方向に1.5m飛ばしてチェック
        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.5f, checkDir, out RaycastHit hit, 1.5f, obstacleMask))
        {
            Vector3 wallNormal = hit.normal;
            wallNormal.y = 0;

            // 進行方向のベクトルから、壁にめり込む力を完全に消し去る（壁に平行なベクトルにする）
            Vector3 slideVel = Vector3.ProjectOnPlane(desiredVel, wallNormal);

            // 壁に沿って滑る力 ＋ 壁から少し押し返す力（反発力）を混ぜる
            desiredVel = (slideVel.normalized * tankStatus.GetCurrentMoveSpeed()) + (wallNormal * 2.0f);
        }

        // --- プレイヤー同様の「向いてから移動する」滑らかな移動処理 ---
        if (desiredVel.magnitude > 0.1f)
        {
            Vector3 moveDir = desiredVel.normalized;

            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float currentY = _rb.rotation.eulerAngles.y;

            float rotSpeed = tankStatus.GetCurrentRotationSpeed();
            float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, rotSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            if (Mathf.Abs(Mathf.DeltaAngle(nextAngle, targetAngle)) <= 45.0f)
            {
                float speed = tankStatus.GetCurrentMoveSpeed();
                Vector3 vel = transform.forward * speed;
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
            }
            else
            {
                Vector3 vel = transform.forward * (tankStatus.GetCurrentMoveSpeed() * 0.3f);
                _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
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

    [Tooltip("砲塔の回転とターゲティングを行うAI処理")]
    private void HandleTurretAI()
    {
        ThinkTarget();

        if (turretTransform == null) return;

        Vector3 targetDir = Vector3.forward;
        if (_currentTarget != null)
        {
            targetDir = (_currentTarget.transform.position - turretTransform.position).normalized;

            if (enemyData.useSmartRicochet)
            {
                _smartAimTimer -= Time.deltaTime;
                // ★修正: 索敵頻度を0.2秒から「0.1秒」に上げ、より素早くルートを見つける
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
            // 索敵範囲を広めにとり、周囲を常に警戒しているように見せる
            offsetAngle = noise * (enemyData.turretSearchAngle + 30f);
        }

        if (targetDir != Vector3.zero)
        {
            Quaternion baseRot = Quaternion.LookRotation(targetDir);
            Quaternion finalRot = baseRot * Quaternion.Euler(0, offsetAngle, 0);

            // ロックオン時の減速を廃止し、設定された速度で素早く向くようにしました
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

    [Tooltip("射撃の試行（射線と弾数、クールタイムの確認）")]
    public void TryFire()
    {
        // ★修正: 弾数チェックに加えて、元々の「敵の攻撃クールタイム(_fireCooldownTimer)」も判定に戻す
        if (tankStatus.IsInStun || _isActionRigid || _fireCooldownTimer > 0 || _currentAmmoCount <= 0) return;

        // 壁埋まりチェック
        int wallLayerMask = LayerMask.GetMask("Wall");
        Vector3 muzzlePos = firePoint.position;
        Vector3 turretCenter = turretTransform != null ? turretTransform.position : transform.position;

        if (Physics.CheckSphere(muzzlePos, 0.2f, wallLayerMask)) return;
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

    [Tooltip("現在向いている方向に撃った場合、敵に当たるか（射線が通っているか）を判定する")]
    private bool CheckShootTrajectory()
    {
        if (_currentTarget == null || firePoint == null) return false;

        Vector3 startPos = firePoint.position;
        Vector3 dir = firePoint.forward;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike") & ~LayerMask.GetMask("Mine") & ~LayerMask.GetMask("Ignore Raycast");

        // ★スマートエイムONで、正解の角度が見つかっている場合
        if (enemyData.useSmartRicochet && _smartAimDir != Vector3.zero)
        {
            // ▼ここで shotAllowAngle が使用されます！
            // 目標の角度に近ければ（設定された許容角度以内なら）、撃つと判定する
            float angleDiff = Vector3.Angle(dir, _smartAimDir);
            if (angleDiff <= enemyData.shotAllowAngle)
            {
                // 撃つ瞬間に砲塔を正解の角度へ「カチッ」と強制的に合わせる（エイム補正による必中化）
                if (turretTransform != null)
                {
                    turretTransform.rotation = Quaternion.LookRotation(_smartAimDir);
                    _independentTurretRotation = turretTransform.rotation;
                }
                return true;
            }
            return false; // 正解の角度に向くまで無駄撃ちしない
        }

        // ★スマートエイムOFF時、または直接狙う時
        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (!enemyData.considerReflection) maxBounces = 0;

        // 直接狙う時も細いRaycastで正確に判定する
        return SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0);
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
                TeamType mineTeam = TeamType.Neutral;
                var mineCtrl = hit.GetComponent<MineController>();
                if (mineCtrl != null) mineTeam = mineCtrl.GetTeam();
                else
                {
                    var robot = hit.GetComponent<RobotBombController>();
                    if (robot != null) mineTeam = robot.GetTeam();
                }

                // 味方の地雷（自身が置いたもの含む）かどうかの判定
                bool isAlly = (mineTeam == tankStatus.team);
                float avoidRadius = isAlly ? enemyData.allyMineAvoidRadius : enemyData.mineAvoidRadius;

                // 範囲内なら避ける
                if (avoidRadius > 0 && dist < avoidRadius)
                {
                    float weight = 1.0f - (dist / avoidRadius);
                    // ★修正②: 巻き込まれないように、地雷から離れる力（weightへの掛算）を「5.0f」に強化
                    totalAvoidVec += awayDir * weight * 5.0f;
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

    // --- 跳弾ルート探索（スマートエイム） ---


    [Tooltip("360度全方位をスキャンして、跳弾でプレイヤーに当たる角度を探し出す")]
    private Vector3 FindSmartRicochetDirection()
    {
        if (firePoint == null || _currentTarget == null) return Vector3.zero;

        int maxBounces = (tankStatus.GetShellPrefab()?.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0) + tankStatus.bonusBounces;
        if (maxBounces <= 0 || !enemyData.considerReflection) return Vector3.zero;

        Vector3 startPos = firePoint.position;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike") & ~LayerMask.GetMask("Mine") & ~LayerMask.GetMask("Ignore Raycast");

        // ★修正: プレイヤーの方角だけでなく「360度全方位」を3度刻みでスキャンする！
        // 真横や真後ろの壁を使った跳弾も完璧に計算できるようになります。
        for (int angle = 0; angle < 360; angle += 3)
        {
            Vector3 testDir = Quaternion.Euler(0, angle, 0) * Vector3.forward;

            if (SimulateRaycastTrajectory(startPos, testDir, maxBounces, layerMask, 0))
            {
                return testDir; // 完璧な角度を発見！
            }
        }
        return Vector3.zero;
    }

    [Tooltip("仮想的に細い線を飛ばして跳弾をシミュレーションし、敵に当たるか判定する")]
    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce)
    {
        if (currentBounce > 5) return false; // 無限ループ防止

        // ★修正: SphereCast(太い線)ではなく、Raycast(極細の線)を使うことで、床への誤衝突や角への引っかかり、自分自身への誤射判定を完全に無くす
        if (Physics.Raycast(startPos, dir, out RaycastHit hit, 100f, layerMask))
        {
            // 壁に当たった場合
            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                if (bouncesLeft > 0)
                {
                    // 反射ベクトルを計算して再帰呼び出し
                    Vector3 reflectDir = Vector3.Reflect(dir, hit.normal);
                    reflectDir.y = 0;
                    // 反射位置を少しだけ壁から浮かせる（0.05f）ことで、壁抜けを防ぐ
                    return SimulateRaycastTrajectory(hit.point + hit.normal * 0.05f, reflectDir, bouncesLeft - 1, layerMask, currentBounce + 1);
                }
                return false;
            }

            // 戦車に当たった場合
            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null)
            {
                if (hitTank.team != tankStatus.team)
                {
                    // 敵に当たるなら文句なしで射撃可能！(太い判定での味方誤射チェックは排除しました)
                    return true;
                }
                else
                {
                    // 味方に当たる場合は撃たない
                    return false;
                }
            }
        }
        return false;
    }
}