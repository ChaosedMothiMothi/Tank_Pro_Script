using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// パーティタンク（本体である大型個体が、後ろのコアを牽引する）の統合コントローラー。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(NavMeshAgent), typeof(LineRenderer))]
public class PartyBossController : MonoBehaviour
{
    // ==========================================
    // インスペクター設定項目
    // ==========================================
    [Header("基本設定")]
    public TankStatus tankStatus;
    [Tooltip("本体(牽引する大型個体)のAI設定")]
    public EnemyData mainEnemyData;
    [Tooltip("牽引されるコアのAI設定")]
    public EnemyData coreEnemyData;

    [Header("牽引されるコア(後衛)の設定")]
    [Tooltip("コアのRigidbody")]
    public Rigidbody coreUnitRb;
    [Tooltip("コアの砲塔")]
    public Transform coreTurret;
    [Tooltip("コアの射撃口")]
    public Transform coreFirePoint;
    [Tooltip("コアのマズルフラッシュ発生位置（未設定なら射撃口と同じ）")]
    public Transform coreMuzzleFlashPoint;
    [Tooltip("コアが撃つ弾のプレハブ")]
    public GameObject coreShellPrefab;
    [Tooltip("コアが自律的に移動しようとする力の強さ（0.0〜1.0）")]
    [Range(0f, 1f)] public float coreMovePower = 0.5f;
    [Tooltip("牽引の紐の長さ（これ以上離れると引っ張られる）")]
    public float towDistance = 3.5f;

    [Header("本体(大型/前衛)の武装：5Way射撃")]
    [Tooltip("本体の砲塔")]
    public Transform mainTurret;
    [Tooltip("本体の射撃口（5つ）")]
    public Transform[] mainFirePoints;
    [Tooltip("本体のマズルフラッシュ発生位置")]
    public Transform[] mainMuzzleFlashPoints;
    [Tooltip("本体が撃つ弾のプレハブ")]
    public GameObject mainShellPrefab;

    // ★追加: 砲塔の索敵用変数
    private float _mainTurretNoiseTime;
    private float _coreTurretNoiseTime;
    private bool _isMainCanShoot = false;
    private bool _isCoreCanShoot = false;

    [Header("本体(大型/前衛)の武装：火炎放射")]
    public Transform flamethrowerPoint;
    public float flameDetectRadius = 8.0f;
    public float flameDuration = 3.0f;
    public float flameCooldown = 6.0f;
    public GameObject flameShellPrefab;
    public float flameFireRate = 10f;

    [Header("エフェクト・装飾リソース")]
    [Tooltip("コアが投下する地雷のプレハブ")]
    public GameObject minePrefab;
    [Tooltip("牽引紐の描画マテリアル")]
    public Material towLineMaterial;

    [Tooltip("本体の車輪(左)")]
    public Transform[] mainLeftWheels;
    [Tooltip("本体の車輪(右)")]
    public Transform[] mainRightWheels;

    // ★追加: 車輪回転の速度設定
    [Tooltip("本体が前進・後退する時の車輪の回転速度係数")]
    public float wheelMoveSpinSpeed = 500f;
    [Tooltip("本体がその場で旋回する時の車輪の回転速度係数")]
    public float wheelTurnSpinSpeed = 0.5f;

    private float _mainStuckTimer = 0f;

    // ★追加: 射撃硬直管理
    private float _mainShotRigidTimer = 0f;
    private float _coreShotRigidTimer = 0f;

    // ★追加: 自弾との衝突を一時的に無効化する時間
    [Header("自弾との衝突保護")]
    [Tooltip("本体が発射した弾と本体コリジョンの衝突を無効化する秒数")]
    public float selfShellIgnoreTime = 0.2f;

    // ==========================================
    // 内部変数
    // ==========================================
    private Rigidbody _mainRb;
    private NavMeshAgent _agent;
    private LineRenderer _lineRenderer;
    private TankStatus _currentTarget;

    // --- 移動・AI管理 ---
    private float _mainMoveTimer;
    private Vector3 _mainMoveTarget;
    private Vector3 _mainSmoothedMoveDir;

    private float _coreMoveTimer;
    private Vector3 _coreMoveTarget;
    private Vector3 _coreSmoothedMoveDir;

    private Queue<Vector3> _bodyPathHistory = new Queue<Vector3>();
    private float _pathRecordTimer = 0f;

    // --- クールダウン・弾数管理 ---
    private float _currentMainFireCooldown;
    private float _currentCoreFireCooldown;
    private float _currentFlameCooldown;
    private int _mainAmmoCount;
    private int _coreAmmoCount;

    // --- 砲塔・攻撃・車輪状態管理 ---
    private bool _isSpinningMode = false;
    private bool _isFlaming = false;
    private Quaternion _mainTurretRotation;
    private Quaternion _coreTurretRotation;

    private Vector3 _coreSmartAimDir = Vector3.zero;
    private float _coreSmartAimTimer = 0f;

    private Vector3 _mainLastPos;
    private float _mainLastYRot;

    // ==========================================
    // 初期化処理
    // ==========================================
    private void Awake()
    {
        _mainRb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
        _lineRenderer = GetComponent<LineRenderer>();

        // ★ 修正: Awakeの時点で即座に親子関係を解除し、親のScaleやRotationの影響を断ち切る
        if (coreUnitRb != null)
        {
            coreUnitRb.transform.SetParent(null); // StartからAwakeの一番上に移動

            var joint = coreUnitRb.GetComponent<Joint>();
            if (joint != null) Destroy(joint);
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
        // ★修正: スポーン時に地面に埋まらないように、コライダーの底辺を計算して高さを補正する
        AdjustSpawnHeight(transform, _agent);

        if (coreUnitRb != null)
        {
            // コア側も同様に高さを補正する
            AdjustSpawnHeight(coreUnitRb.transform, null);

            coreUnitRb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            DamageForwarder forwarder = coreUnitRb.gameObject.AddComponent<DamageForwarder>();
            forwarder.mainTankStatus = tankStatus;

            coreUnitRb.gameObject.tag = "Tank";
            coreUnitRb.gameObject.layer = gameObject.layer;
            foreach (Transform child in coreUnitRb.transform)
            {
                child.gameObject.tag = "Tank";
                child.gameObject.layer = gameObject.layer;
            }
        }

        // ★追加: 車輪についているコライダーが物理演算の邪魔をして移動を止めるのを防ぐため、強制的にTriggerにする
        DisableWheelColliders(mainLeftWheels);
        DisableWheelColliders(mainRightWheels);

        _lineRenderer.startWidth = 0.1f;
        _lineRenderer.endWidth = 0.1f;
        if (towLineMaterial != null) _lineRenderer.material = towLineMaterial;

        if (mainTurret != null) _mainTurretRotation = mainTurret.rotation;
        if (coreTurret != null) _coreTurretRotation = coreTurret.rotation;

        DecideNextMoveTarget(ref _mainMoveTarget, ref _mainMoveTimer, transform.position, mainEnemyData);
        DecideNextMoveTarget(ref _coreMoveTarget, ref _coreMoveTimer, coreUnitRb != null ? coreUnitRb.position : transform.position, coreEnemyData);

        _mainAmmoCount = tankStatus.GetTotalMaxAmmo();
        _coreAmmoCount = tankStatus.GetTotalMaxAmmo();

        _mainLastPos = transform.position;
        _mainLastYRot = transform.eulerAngles.y;

        StartCoroutine(TurretBehaviorRoutine());
        StartCoroutine(MineDropRoutine());
    }

    // ★追加: コライダーの底を計算し、NavMeshの床にぴったり乗せるメソッド
    private void AdjustSpawnHeight(Transform target, NavMeshAgent agent)
    {
        if (UnityEngine.AI.NavMesh.SamplePosition(target.position, out UnityEngine.AI.NavMeshHit navHit, 10.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            float offsetY = 0f;
            Collider[] cols = target.GetComponentsInChildren<Collider>();
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
                offsetY = target.position.y - minColY;
            }

            Vector3 groundPos = new Vector3(target.position.x, navHit.position.y + offsetY + 0.05f, target.position.z);
            target.position = groundPos;

            if (agent != null && agent.enabled)
            {
                agent.Warp(groundPos);
            }
        }
    }

    // ★追加: 車輪コライダー無効化用メソッド
    private void DisableWheelColliders(Transform[] wheels)
    {
        if (wheels == null) return;
        foreach (var w in wheels)
        {
            if (w == null) continue;
            Collider[] cols = w.GetComponentsInChildren<Collider>();
            foreach (var c in cols) c.isTrigger = true; // 物理的な衝突（引っかかり）を消す
        }
    }

    // ==========================================
    // フレーム更新処理
    // ==========================================
    private void Update()
    {
        if (tankStatus.IsDead || GameManager.Instance == null || !GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished())
        {
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            return;
        }

        DrawTowLine();
        ThinkTarget();
        ThinkMainMoveLogic();
        ThinkCoreMoveLogic();

        if (_currentMainFireCooldown > 0) _currentMainFireCooldown -= Time.deltaTime;
        if (_currentCoreFireCooldown > 0) _currentCoreFireCooldown -= Time.deltaTime;
        if (_currentFlameCooldown > 0) _currentFlameCooldown -= Time.deltaTime;

        HandleTurretLogic();
        CheckAndUseFlamethrower();
        UpdateWheelRotation();

        if (_mainShotRigidTimer > 0f) _mainShotRigidTimer -= Time.deltaTime;
        if (_coreShotRigidTimer > 0f) _coreShotRigidTimer -= Time.deltaTime;

    }

    private void FixedUpdate()
    {
        if (tankStatus.IsDead || tankStatus.IsInStun || GameManager.Instance == null || !GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished())
        {
            _mainRb.linearVelocity = new Vector3(0, _mainRb.linearVelocity.y, 0);
            if (coreUnitRb != null) coreUnitRb.linearVelocity = new Vector3(0, coreUnitRb.linearVelocity.y, 0);
            return;
        }
        // ★追加: 段差などでNavMeshから外れてしまっても、自動で道の上に復帰させる（スタック・完全停止防止）
        if (_agent != null && !_agent.isOnNavMesh)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(_mainRb.position, out UnityEngine.AI.NavMeshHit hit, 2.0f, UnityEngine.AI.NavMesh.AllAreas))
            {
                _agent.Warp(hit.position);
            }
        }

        ExecuteMainMovement();
        ExecuteCoreMovement(); // ★牽引と自律移動をここで同時に行うように統合しました

        _pathRecordTimer += Time.fixedDeltaTime;
        if (_pathRecordTimer >= 0.2f)
        {
            _bodyPathHistory.Enqueue(transform.position);
            if (_bodyPathHistory.Count > 10) _bodyPathHistory.Dequeue();
            _pathRecordTimer = 0f;
        }

        // ==========================================================
        // ★追加：コアの「お辞儀」「傾き」を毎フレーム物理的に強制リセットする
        // ==========================================================
        if (coreUnitRb != null)
        {
            // 現在のY軸の向き（旋回角度）だけを残し、X軸（お辞儀）とZ軸（横傾き）を強制的に0にする
            float currentYAngle = coreUnitRb.rotation.eulerAngles.y;
            coreUnitRb.rotation = Quaternion.Euler(0, currentYAngle, 0);
        }
    }

    private void LateUpdate()
    {
        if (tankStatus.IsDead || tankStatus.IsInStun || GameManager.Instance == null || !GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished()) return;

        if (_isSpinningMode && mainTurret != null)
        {
            mainTurret.localRotation = Quaternion.Inverse(transform.rotation) * _mainTurretRotation;
        }
    }

    // ==========================================
    // 装飾：車輪の回転アニメーション（本体のみ）
    // ==========================================
    private void UpdateWheelRotation()
    {
        // 本体の前進距離と旋回量を取得
        Vector3 mainDeltaPos = transform.position - _mainLastPos;
        float mainFwdMove = Vector3.Dot(mainDeltaPos, transform.forward);
        float mainDeltaRot = Mathf.DeltaAngle(_mainLastYRot, transform.eulerAngles.y);

        _mainLastPos = transform.position;
        _mainLastYRot = transform.eulerAngles.y;

        // インスペクターで設定した係数をもとに、左右の車輪の回転量を算出
        // 左車輪は「前進＋右旋回」で正回転、右車輪は「前進＋左旋回」で正回転する
        float mainLeftSpin = (mainFwdMove * wheelMoveSpinSpeed) + (mainDeltaRot * wheelTurnSpinSpeed);
        float mainRightSpin = (mainFwdMove * wheelMoveSpinSpeed) - (mainDeltaRot * wheelTurnSpinSpeed);

        RotateWheels(mainLeftWheels, mainLeftSpin);
        RotateWheels(mainRightWheels, mainRightSpin);
    }

    private void RotateWheels(Transform[] wheels, float spinAmount)
    {
        // わずかなブレによるガタつきを防ぐため、一定以下の回転量は無視
        if (wheels == null || Mathf.Abs(spinAmount) < 0.01f) return;

        foreach (var w in wheels)
        {
            if (w != null)
            {
                // 親のScaleによる歪みを防ぐため、ローカルオイラー角のX軸に直接足し算する
                Vector3 euler = w.localEulerAngles;
                euler.x += spinAmount;
                w.localEulerAngles = euler;
            }
        }
    }

    private void DrawTowLine()
    {
        if (coreUnitRb != null)
        {
            _lineRenderer.enabled = true;
            _lineRenderer.SetPosition(0, transform.position + Vector3.up * 0.5f);
            _lineRenderer.SetPosition(1, coreUnitRb.position + Vector3.up * 0.5f);
        }
        else _lineRenderer.enabled = false;
    }

    // ==========================================
    // AI思考（移動・回避ロジック）
    // ==========================================
    private void ThinkTarget()
    {
        _currentTarget = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            .Where(t => t != null && t.team != tankStatus.team && !t.IsDead)
            .OrderBy(t => Vector3.Distance(transform.position, t.transform.position))
            .FirstOrDefault();
    }

    private void ThinkMainMoveLogic()
    {
        if (mainEnemyData == null) return;
        _mainMoveTimer += Time.deltaTime;

        if (_agent != null && _agent.isOnNavMesh)
        {
            Vector3 finalDest = CalculateBehaviorTarget(transform.position, ref _mainMoveTarget, ref _mainMoveTimer, mainEnemyData);
            _agent.SetDestination(finalDest);
        }
    }

    private void ThinkCoreMoveLogic()
    {
        if (coreEnemyData == null || coreUnitRb == null) return;
        _coreMoveTimer += Time.deltaTime;
        _coreMoveTarget = CalculateBehaviorTarget(coreUnitRb.position, ref _coreMoveTarget, ref _coreMoveTimer, coreEnemyData);
    }

    private Vector3 CalculateBehaviorTarget(Vector3 myPos, ref Vector3 currentTarget, ref float timer, EnemyData data)
    {
        Vector3 finalDest = currentTarget;

        if (data.aiType == EnemyData.AIType.Coward && _currentTarget != null && Vector3.Distance(myPos, _currentTarget.transform.position) < 12.0f)
        {
            // ★修正: 臆病（コアなど）は、敵から逃げつつランダムに左右へブレる（ジグザグに回避する）
            Vector3 awayDir = (myPos - _currentTarget.transform.position).normalized;
            Vector3 randomWobble = new Vector3(Mathf.Sin(Time.time * 3f + Random.value), 0, Mathf.Cos(Time.time * 3f + Random.value)) * 0.8f;
            Vector3 runDir = (awayDir + randomWobble).normalized;

            Vector3 targetPos = myPos + runDir * 6.0f;
            if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 4.0f, NavMesh.AllAreas)) finalDest = hit.position;
        }
        else if (data.aiType == EnemyData.AIType.Aggressive && _currentTarget != null)
        {
            finalDest = _currentTarget.transform.position;
        }
        else if (timer > 5.0f || Vector3.Distance(myPos, currentTarget) < 2.0f)
        {
            DecideNextMoveTarget(ref currentTarget, ref timer, myPos, data);
            finalDest = currentTarget;
        }

        return finalDest;
    }

    private void DecideNextMoveTarget(ref Vector3 targetPos, ref float timer, Vector3 myPos, EnemyData data)
    {
        timer = 0f;
        if (data == null) return;

        // ★修正: 目的地が近すぎてスタックしないよう、最低でも3m以上離れた場所を探す
        for (int i = 0; i < 5; i++)
        {
            Vector2 randCircle = Random.insideUnitCircle * 15f;
            Vector3 randomPos = myPos + new Vector3(randCircle.x, 0, randCircle.y);

            if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 10.0f, NavMesh.AllAreas))
            {
                if (Vector3.Distance(myPos, hit.position) > 3.0f)
                {
                    targetPos = hit.position;
                    return; // 良い場所が見つかったら終了
                }
            }
        }

        // 5回探して見つからなければ、強制的に5m前方を目的地にしてスタックを回避
        targetPos = myPos + transform.forward * 5f;
    }

    // ==========================================
    // 物理移動実行
    // ==========================================
    private void ExecuteMainMovement()
    {
        if (_mainRb == null) return;

        // ★修正:
        // その場回転攻撃中(_isSpinningMode)は止めない
        // 実際に弾を撃った後の硬直タイマー中だけ停止
        if (_mainShotRigidTimer > 0f || _isFlaming)
        {
            _mainRb.linearVelocity = new Vector3(0, _mainRb.linearVelocity.y, 0);
            _mainStuckTimer = 0f;
            return;
        }

        Vector3 baseDir = Vector3.zero;

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            baseDir = _agent.desiredVelocity;
        }

        if (baseDir.magnitude < 0.1f)
        {
            Vector3 toTarget = _mainMoveTarget - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude > 1.0f)
            {
                baseDir = toTarget.normalized;
            }
            else
            {
                DecideNextMoveTarget(ref _mainMoveTarget, ref _mainMoveTimer, transform.position, mainEnemyData);
                _mainRb.linearVelocity = new Vector3(0, _mainRb.linearVelocity.y, 0);
                return;
            }
        }
        else
        {
            baseDir.Normalize();
        }

        Vector3 finalDir = ApplyAvoidance(baseDir, transform.position, transform.forward, mainEnemyData);
        if (finalDir == Vector3.zero) finalDir = baseDir;

        _mainSmoothedMoveDir = Vector3.Lerp(
            _mainSmoothedMoveDir == Vector3.zero ? transform.forward : _mainSmoothedMoveDir,
            finalDir.normalized,
            Time.fixedDeltaTime * 5.0f
        ).normalized;

        float targetAngle = Mathf.Atan2(_mainSmoothedMoveDir.x, _mainSmoothedMoveDir.z) * Mathf.Rad2Deg;
        float currentY = _mainRb.rotation.eulerAngles.y;
        float nextAngle = Mathf.MoveTowardsAngle(
            currentY,
            targetAngle,
            tankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime
        );

        _mainRb.MoveRotation(Quaternion.Euler(0f, nextAngle, 0f));

        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle));

        float moveScale = 1f;
        if (angleDiff > 90f) moveScale = 0f;
        else if (angleDiff > 45f) moveScale = 0.35f;
        else if (angleDiff > 20f) moveScale = 0.7f;

        Vector3 moveForward = Quaternion.Euler(0f, nextAngle, 0f) * Vector3.forward;
        Vector3 vel = moveForward * (tankStatus.GetCurrentMoveSpeed() * moveScale);
        _mainRb.linearVelocity = new Vector3(vel.x, _mainRb.linearVelocity.y, vel.z);

        Vector3 planarVel = new Vector3(_mainRb.linearVelocity.x, 0f, _mainRb.linearVelocity.z);
        if (planarVel.magnitude < 0.15f)
        {
            _mainStuckTimer += Time.fixedDeltaTime;
            if (_mainStuckTimer > 1.0f)
            {
                DecideNextMoveTarget(ref _mainMoveTarget, ref _mainMoveTimer, transform.position, mainEnemyData);
                _mainStuckTimer = 0f;
            }
        }
        else
        {
            _mainStuckTimer = 0f;
        }

        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            _agent.nextPosition = _mainRb.position;
        }
    }

    private void ExecuteCoreMovement()
    {
        if (coreUnitRb == null || coreEnemyData == null) return;

        // ==========================================
        // 1. 目的地更新
        // ==========================================
        Vector3 toTarget = _coreMoveTarget - coreUnitRb.position;
        toTarget.y = 0f;

        if (_coreMoveTimer > 5.0f || toTarget.magnitude < 2.0f)
        {
            DecideNextMoveTarget(ref _coreMoveTarget, ref _coreMoveTimer, coreUnitRb.position, coreEnemyData);
            toTarget = _coreMoveTarget - coreUnitRb.position;
            toTarget.y = 0f;
        }

        Vector3 baseDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : coreUnitRb.transform.forward;

        // ==========================================
        // 2. 通常敵戦車同様、回避込みの進行方向を作る
        // ==========================================
        Vector3 finalDir = ApplyAvoidance(baseDir, coreUnitRb.position, coreUnitRb.transform.forward, coreEnemyData);
        if (finalDir == Vector3.zero) finalDir = baseDir;

        _coreSmoothedMoveDir = Vector3.Lerp(
            _coreSmoothedMoveDir == Vector3.zero ? coreUnitRb.transform.forward : _coreSmoothedMoveDir,
            finalDir.normalized,
            Time.fixedDeltaTime * 6.0f
        ).normalized;

        // ==========================================
        // 3. 車体回転
        // ==========================================
        float targetAngle = Mathf.Atan2(_coreSmoothedMoveDir.x, _coreSmoothedMoveDir.z) * Mathf.Rad2Deg;
        float currentY = coreUnitRb.rotation.eulerAngles.y;
        float nextAngle = Mathf.MoveTowardsAngle(
            currentY,
            targetAngle,
            tankStatus.GetCurrentRotationSpeed() * Time.fixedDeltaTime
        );

        coreUnitRb.MoveRotation(Quaternion.Euler(0f, nextAngle, 0f));

        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle));

        // ==========================================
        // 4. 自律移動速度
        // ==========================================
        float selfMoveScale = 1f;
        if (angleDiff > 90f) selfMoveScale = 0f;
        else if (angleDiff > 45f) selfMoveScale = 0.35f;
        else if (angleDiff > 20f) selfMoveScale = 0.7f;

        Vector3 selfMoveForward = Quaternion.Euler(0f, nextAngle, 0f) * Vector3.forward;
        Vector3 selfVelocity = selfMoveForward * (tankStatus.GetCurrentMoveSpeed() * coreMovePower * selfMoveScale);

        // ==========================================
        // 5. 牽引補正
        // ==========================================
        Vector3 towVelocity = Vector3.zero;
        Vector3 pullTarget = transform.position;

        int obstacleMask = LayerMask.GetMask("Wall");
        if (Physics.Linecast(coreUnitRb.position, transform.position, obstacleMask))
        {
            if (_bodyPathHistory.Count > 0)
                pullTarget = _bodyPathHistory.Peek();
        }

        Vector3 offset = pullTarget - coreUnitRb.position;
        offset.y = 0f;

        if (offset.magnitude > towDistance)
        {
            float pullStrength = Mathf.Clamp((offset.magnitude - towDistance) * 2.0f, 0.5f, 5.0f);
            towVelocity = offset.normalized * (tankStatus.GetCurrentMoveSpeed() * pullStrength);
        }

        // ==========================================
        // 6. 合成
        // ==========================================
        Vector3 finalVelocity = selfVelocity + towVelocity;
        coreUnitRb.linearVelocity = new Vector3(finalVelocity.x, coreUnitRb.linearVelocity.y, finalVelocity.z);
    }

    private Vector3 ApplyAvoidance(Vector3 baseDir, Vector3 pos, Vector3 forwardDir, EnemyData data)
    {
        Vector3 finalDir = baseDir.normalized;
        if (finalDir == Vector3.zero) finalDir = forwardDir.normalized;

        // 戦車回避
        Vector3 tankAvoid = GetAvoidanceVector(pos, "Tank", data);
        if (tankAvoid != Vector3.zero)
        {
            finalDir = (finalDir + tankAvoid * 2.5f).normalized;
        }

        // 弾・地雷回避
        Vector3 deadlyAvoid = GetAvoidanceVector(pos, "Deadly", data);
        if (deadlyAvoid != Vector3.zero)
        {
            finalDir = (finalDir * 0.6f + deadlyAvoid * 3.5f).normalized;
        }

        // 壁回避
        Vector3 wallAvoid = GetWallAvoidanceVector(pos, forwardDir, 3.5f);
        if (wallAvoid != Vector3.zero)
        {
            finalDir = (finalDir * 0.5f + wallAvoid * 2.8f).normalized;
        }

        if (finalDir.sqrMagnitude < 0.001f)
            finalDir = forwardDir.normalized;

        return finalDir;
    }

    private Vector3 GetAvoidanceVector(Vector3 pos, string type, EnemyData data)
    {
        float shellAvoid = data != null ? data.shellAvoidRadius : 3.0f;
        float mineAvoid = data != null ? data.mineAvoidRadius : 3.0f;
        float allyMineAvoid = data != null ? data.allyMineAvoidRadius : 3.0f;

        float maxSearchRadius = 3.5f;
        maxSearchRadius = Mathf.Max(maxSearchRadius, shellAvoid, mineAvoid, allyMineAvoid);

        Collider[] hits = Physics.OverlapSphere(pos, maxSearchRadius);
        Vector3 avoidVec = Vector3.zero;

        foreach (var hit in hits)
        {
            if (hit == null) continue;

            if (hit.gameObject == gameObject) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            if (coreUnitRb != null && hit.transform.IsChildOf(coreUnitRb.transform)) continue;

            Vector3 toObj = hit.transform.position - pos;
            toObj.y = 0f;
            float dist = toObj.magnitude;
            if (dist <= 0.001f) continue;

            Vector3 awayDir = -toObj.normalized;

            if (type == "Deadly")
            {
                if (hit.CompareTag("Shell"))
                {
                    if (dist < shellAvoid)
                    {
                        avoidVec += awayDir * (1.0f - dist / shellAvoid);
                    }
                }
                else if (hit.CompareTag("Mine"))
                {
                    TankStatus mineOwner = hit.GetComponentInParent<TankStatus>();
                    float radius = mineAvoid;

                    if (mineOwner != null && mineOwner.team == tankStatus.team)
                        radius = allyMineAvoid;

                    if (dist < radius)
                    {
                        avoidVec += awayDir * (1.0f - dist / radius);
                    }
                }
            }
            else if (type == "Tank")
            {
                TankStatus otherTank = hit.GetComponentInParent<TankStatus>();
                if (otherTank != null && !otherTank.IsDead && otherTank != tankStatus)
                {
                    float tankAvoidRadius = 2.5f;
                    if (dist < tankAvoidRadius)
                    {
                        avoidVec += awayDir * (1.0f - dist / tankAvoidRadius);
                    }
                }
            }
        }

        avoidVec.y = 0f;
        return avoidVec;
    }

    private Vector3 GetWallAvoidanceVector(Vector3 pos, Vector3 forwardDir, float maxDist)
    {
        Vector3 avoidVec = Vector3.zero;
        int obstacleMask = LayerMask.GetMask("Wall", "Spike");

        float[] angles = { 0f, 20f, -20f, 45f, -45f, 70f, -70f };

        foreach (float angle in angles)
        {
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forwardDir.normalized;
            float checkDist = Mathf.Abs(angle) >= 45f ? maxDist * 0.75f : maxDist;

            if (Physics.Raycast(pos + Vector3.up * 0.5f, dir, out RaycastHit hit, checkDist, obstacleMask))
            {
                float strength = 1.0f - (hit.distance / checkDist);
                Vector3 normal = hit.normal;
                normal.y = 0f;
                avoidVec += normal.normalized * strength;
            }
        }

        avoidVec.y = 0f;
        return avoidVec;
    }

    // ==========================================
    // 武装：地雷投下（コア）
    // ==========================================
    private IEnumerator MineDropRoutine()
    {
        while (!tankStatus.IsDead)
        {
            while (GameManager.Instance == null || !GameManager.Instance.IsGameStarted) yield return null;

            yield return new WaitForSeconds(Random.Range(4.0f, 7.0f));
            if (minePrefab != null && tankStatus.ActiveMineCount < tankStatus.GetTotalMineLimit())
            {
                Vector3 spawnPos = coreUnitRb != null ? coreUnitRb.position : transform.position;

                if (Physics.OverlapSphere(spawnPos, coreEnemyData != null ? coreEnemyData.minePlacementSpacing : 2.0f).Any(c => c.CompareTag("Mine"))) continue;

                GameObject mine = Instantiate(minePrefab, spawnPos, Quaternion.identity);

                Collider[] mineCols = mine.GetComponentsInChildren<Collider>();
                Collider[] myCols = transform.root.GetComponentsInChildren<Collider>();
                if (coreUnitRb != null)
                {
                    Collider[] coreCols = coreUnitRb.GetComponentsInChildren<Collider>();
                    myCols = myCols.Concat(coreCols).ToArray();
                }

                foreach (var mc in mineCols)
                {
                    foreach (var my in myCols)
                    {
                        if (mc != null && my != null) Physics.IgnoreCollision(mc, my, true);
                    }
                }

                if (mine.TryGetComponent(out MineController mcController))
                {
                    mcController.Init(tankStatus, tankStatus.GetMineData());
                    tankStatus.OnMinePlaced();
                }
                else if (mine.TryGetComponent(out RobotBombController robotBomb))
                {
                    robotBomb.Init(tankStatus, tankStatus.GetMineData());
                    tankStatus.OnMinePlaced();
                }
                else if (mine.TryGetComponent(out TankSpawnerBox spawnerBox))
                {
                    spawnerBox.Init(tankStatus, tankStatus.team);
                    tankStatus.OnMinePlaced();
                }
            }
        }
    }

    // ==========================================
    // 武装：砲塔回転制御
    // ==========================================
    private IEnumerator TurretBehaviorRoutine()
    {
        while (!tankStatus.IsDead)
        {
            while (GameManager.Instance == null || !GameManager.Instance.IsGameStarted) yield return null;

            _isSpinningMode = false;
            yield return new WaitForSeconds(Random.Range(4.0f, 6.0f));

            if (_isFlaming) continue;

            _isSpinningMode = true;
            float startAngle = _mainTurretRotation.eulerAngles.y;
            float targetAngle = startAngle + 1080f;
            float currentAngle = startAngle;
            float spinSpeed = (mainEnemyData != null) ? mainEnemyData.turretRotationSpeed : 180f;

            while (currentAngle < targetAngle && !tankStatus.IsDead)
            {
                if (_isFlaming) break;

                currentAngle += spinSpeed * Time.deltaTime;
                _mainTurretRotation = Quaternion.Euler(0, currentAngle, 0);

                if (_currentMainFireCooldown <= 0) TryFire5Way();

                yield return null;
            }
        }
    }

    // ==========================================
    // 砲塔回転制御
    // ==========================================
    private void HandleTurretLogic()
    {
        float mainRotSpeed = (mainEnemyData != null) ? mainEnemyData.turretRotationSpeed : 120f;
        float coreRotSpeed = (coreEnemyData != null) ? coreEnemyData.turretRotationSpeed : 120f;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        // 【コアの砲塔】
        if (coreTurret != null && _currentTarget != null && coreUnitRb != null)
        {
            Vector3 targetDir = _currentTarget.transform.position - coreTurret.position;
            targetDir.y = 0f;

            // ------------------------------------------
            // 1. まずは直接狙う
            // ------------------------------------------
            Vector3 desiredAimDir = targetDir.normalized;

            // ------------------------------------------
            // 2. Smart Ricochet が有効なら跳弾ルート探索
            // ------------------------------------------
            if (coreEnemyData != null && coreEnemyData.useSmartRicochet)
            {
                _coreSmartAimTimer -= Time.deltaTime;
                if (_coreSmartAimTimer <= 0f)
                {
                    _coreSmartAimDir = FindSmartRicochetDirection(coreFirePoint, _currentTarget, coreEnemyData);
                    _coreSmartAimTimer = 0.1f;
                }

                if (_coreSmartAimDir != Vector3.zero)
                {
                    desiredAimDir = _coreSmartAimDir.normalized;
                }
            }

            // ------------------------------------------
            // 3. 今の砲塔向きで撃てるか確認
            // ------------------------------------------
            int coreBounces = (coreShellPrefab != null ? coreShellPrefab.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0 : 0) + tankStatus.bonusBounces;
            if (coreEnemyData != null && !coreEnemyData.considerReflection) coreBounces = 0;

            bool canShootFromCurrentAim = false;
            if (coreFirePoint != null)
            {
                canShootFromCurrentAim = SimulateRaycastTrajectory(coreFirePoint.position, coreFirePoint.forward, coreBounces, layerMask, 0, coreEnemyData);
            }

            // ------------------------------------------
            // 4. 撃てない場合は EnemyData の search angle 分だけ索敵回転
            // ------------------------------------------
            if (!canShootFromCurrentAim && _coreSmartAimDir == Vector3.zero)
            {
                _coreTurretNoiseTime += Time.deltaTime * 0.8f;
                float noise = Mathf.PerlinNoise(_coreTurretNoiseTime, 0f) * 2f - 1f;
                float offset = noise * (coreEnemyData != null ? coreEnemyData.turretSearchAngle : 30f);

                desiredAimDir = Quaternion.Euler(0f, offset, 0f) * targetDir.normalized;
            }

            // ------------------------------------------
            // 5. 砲塔回転
            // ------------------------------------------
            if (desiredAimDir.sqrMagnitude > 0.001f)
            {
                float targetYAngle = Mathf.Atan2(desiredAimDir.x, desiredAimDir.z) * Mathf.Rad2Deg;
                float currentYAngle = coreTurret.eulerAngles.y;
                float nextYAngle = Mathf.MoveTowardsAngle(currentYAngle, targetYAngle, coreRotSpeed * Time.deltaTime);

                coreTurret.rotation = Quaternion.Euler(0f, nextYAngle, 0f);
            }

            if (_currentCoreFireCooldown <= 0)
                TryFireCore();
        }

        // 【本体の砲塔】
        if (_isSpinningMode) return;

        if (_isFlaming && flamethrowerPoint != null && _currentTarget != null)
        {
            // (火炎放射の処理はそのまま)
            Vector3 targetDir = _currentTarget.transform.position - mainTurret.position;
            targetDir.y = 0;

            if (targetDir.magnitude > 0.01f)
            {
                float baseTargetYAngle = Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
                float offsetAngle = -flamethrowerPoint.localEulerAngles.y;
                float finalTargetYAngle = baseTargetYAngle + offsetAngle;

                float currentYAngle = mainTurret.eulerAngles.y;
                float nextYAngle = Mathf.MoveTowardsAngle(currentYAngle, finalTargetYAngle, 200f * Time.deltaTime);

                mainTurret.rotation = Quaternion.Euler(0, nextYAngle, 0);
            }
            return;
        }

        if (mainTurret != null && _currentTarget != null)
        {
            Vector3 targetDir = _currentTarget.transform.position - mainTurret.position;
            targetDir.y = 0;

            // ★追加: 本体の砲塔も、射線が通っていない場合は首を振る
            int mainBounces = (mainShellPrefab != null ? mainShellPrefab.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0 : 0) + tankStatus.bonusBounces;
            if (mainEnemyData != null && !mainEnemyData.considerReflection) mainBounces = 0;

            bool canMainShoot = false;
            if (mainFirePoints != null && mainFirePoints.Length > 0 && mainFirePoints[0] != null)
            {
                canMainShoot = SimulateRaycastTrajectory(mainFirePoints[0].position, mainTurret.forward, mainBounces, layerMask, 0, mainEnemyData);
            }

            if (!canMainShoot)
            {
                _mainTurretNoiseTime += Time.deltaTime * 0.8f;
                float noise = Mathf.PerlinNoise(_mainTurretNoiseTime, 0f) * 2f - 1f;
                float offset = noise * (mainEnemyData != null ? mainEnemyData.turretSearchAngle : 30f);
                targetDir = Quaternion.Euler(0, offset, 0) * targetDir;
            }

            if (targetDir.magnitude > 0.01f)
            {
                float targetYAngle = Mathf.Atan2(targetDir.x, targetDir.z) * Mathf.Rad2Deg;
                float currentYAngle = mainTurret.eulerAngles.y;
                float nextYAngle = Mathf.MoveTowardsAngle(currentYAngle, targetYAngle, mainRotSpeed * Time.deltaTime);

                mainTurret.rotation = Quaternion.Euler(0, nextYAngle, 0);
            }
        }

        if (_currentMainFireCooldown <= 0) TryFire5Way();
    }

    // ==========================================
    // 武装：射撃処理
    // ==========================================
    private void TryFire5Way()
    {
        if (_currentTarget == null || mainTurret == null || mainFirePoints == null || mainFirePoints.Length == 0 || _isFlaming || _mainAmmoCount <= 0) return;

        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");
        bool canShoot = false;

        // ★どれか1つの砲口で撃てるなら全砲口発射
        foreach (var fp in mainFirePoints)
        {
            if (fp == null) continue;

            // ★砲口が壁にめり込んでいたらその砲口は無効
            if (Physics.CheckSphere(fp.position, 0.2f, LayerMask.GetMask("Wall"))) continue;

            // ★コアが射線上にいたらこの砲口は無効（跳弾は考慮しない）
            bool coreBlocking = false;
            RaycastHit[] hits = Physics.SphereCastAll(fp.position, 0.2f, fp.forward, 50f, layerMask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                if (h.distance <= 0f) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;

                if (coreUnitRb != null && h.collider.transform.IsChildOf(coreUnitRb.transform))
                {
                    coreBlocking = true;
                    break;
                }

                if (h.collider.gameObject.layer == LayerMask.NameToLayer("Wall")) break;

                TankStatus ts = h.collider.GetComponentInParent<TankStatus>();
                if (ts != null)
                {
                    if (ts.team != tankStatus.team)
                    {
                        canShoot = true;
                    }
                    break;
                }
            }

            if (!coreBlocking && canShoot) break;
        }

        if (!canShoot) return;

        GameObject shellToUse = mainShellPrefab != null ? mainShellPrefab : tankStatus.GetShellPrefab();
        if (shellToUse == null) return;

        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.ShotSound();
            if (mainMuzzleFlashPoints != null && mainMuzzleFlashPoints.Length > 0)
            {
                foreach (var mfp in mainMuzzleFlashPoints)
                {
                    if (mfp != null) EffectManager.Instance.PlayMuzzleFlash(mfp);
                }
            }
            else
            {
                foreach (var fp in mainFirePoints)
                {
                    if (fp != null) EffectManager.Instance.PlayMuzzleFlash(fp);
                }
            }
        }

        // ★全砲口発射
        foreach (var fp in mainFirePoints)
        {
            if (fp == null) continue;

            // めり込み中の砲口からは出さない
            if (Physics.CheckSphere(fp.position, 0.2f, LayerMask.GetMask("Wall"))) continue;

            GameObject shellObj = Instantiate(shellToUse, fp.position, fp.rotation);
            if (shellObj.TryGetComponent(out ShellController shell))
            {
                shell.Launch(gameObject, 0);
            }

            // ★本体との衝突を短時間だけ無効化
            IgnoreShellCollisionTemporarily(shellObj, selfShellIgnoreTime);
        }

        _currentMainFireCooldown = (mainEnemyData != null) ? mainEnemyData.fireCooldown : 2.0f;
        _mainAmmoCount--;
        StartCoroutine(ReloadMainAmmoRoutine());

        // ★TankStatusの射撃硬直値を参照
        if (tankStatus != null && tankStatus.GetData() != null)
        {
            _mainShotRigidTimer = tankStatus.GetData().shotDelay;
        }
        else
        {
            _mainShotRigidTimer = 0.1f;
        }
    }

    private void TryFireCore()
    {
        if (_currentTarget == null || coreTurret == null || coreFirePoint == null || _coreAmmoCount <= 0) return;

        // 砲口が壁にめり込んでいたら撃たない
        if (Physics.CheckSphere(coreFirePoint.position, 0.2f, LayerMask.GetMask("Wall"))) return;

        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        int maxBounces = (coreShellPrefab != null ? coreShellPrefab.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0 : 0) + tankStatus.bonusBounces;
        if (coreEnemyData != null && !coreEnemyData.considerReflection) maxBounces = 0;

        Vector3 fireDir = coreFirePoint.forward;

        // smart ricochet で見つかった方向があり、砲塔が十分向いていればそれを使う
        if (coreEnemyData != null && coreEnemyData.useSmartRicochet && _coreSmartAimDir != Vector3.zero)
        {
            float angleDiff = Vector3.Angle(coreFirePoint.forward, _coreSmartAimDir);
            if (angleDiff <= coreEnemyData.shotAllowAngle)
            {
                fireDir = _coreSmartAimDir.normalized;
            }
        }

        bool canShoot = SimulateRaycastTrajectory(coreFirePoint.position, fireDir, maxBounces, layerMask, 0, coreEnemyData);
        if (!canShoot) return;

        ExecuteCoreFire();
    }

    private void ExecuteCoreFire()
    {
        _currentCoreFireCooldown = (coreEnemyData != null) ? coreEnemyData.fireCooldown : 1.5f;
        _coreAmmoCount--;
        StartCoroutine(ReloadCoreAmmoRoutine());

        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.ShotSound();
            Transform flashPoint = coreMuzzleFlashPoint != null ? coreMuzzleFlashPoint : coreFirePoint;
            EffectManager.Instance.PlayMuzzleFlash(flashPoint);
        }

        GameObject shellToUse = coreShellPrefab != null ? coreShellPrefab : tankStatus.GetShellPrefab();
        if (shellToUse == null) return;

        GameObject shellObj = Instantiate(shellToUse, coreFirePoint.position, coreFirePoint.rotation);
        if (shellObj.TryGetComponent(out ShellController shell))
        {
            shell.Launch(gameObject, 0);
        }

        if (tankStatus != null && tankStatus.GetData() != null)
        {
            _coreShotRigidTimer = tankStatus.GetData().shotDelay;
        }
        else
        {
            _coreShotRigidTimer = 0.1f;
        }
    }

    private IEnumerator ReloadMainAmmoRoutine()
    {
        yield return new WaitForSeconds(tankStatus.GetData().ammoCooldown);
        if (_mainAmmoCount < tankStatus.GetTotalMaxAmmo()) _mainAmmoCount++;
    }

    private IEnumerator ReloadCoreAmmoRoutine()
    {
        yield return new WaitForSeconds(tankStatus.GetData().ammoCooldown);
        if (_coreAmmoCount < tankStatus.GetTotalMaxAmmo()) _coreAmmoCount++;
    }

    private Vector3 FindSmartRicochetDirection(Transform fp, TankStatus target, EnemyData data)
    {
        if (fp == null || target == null || data == null) return Vector3.zero;

        int maxBounces = (coreShellPrefab != null ? coreShellPrefab.GetComponent<ShellController>()?.shellData?.maxBounces ?? 0 : 0) + tankStatus.bonusBounces;
        if (maxBounces <= 0 || !data.considerReflection) return Vector3.zero;

        Vector3 startPos = fp.position;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        Vector3 baseDir = (target.transform.position - startPos);
        baseDir.y = 0f;
        if (baseDir.sqrMagnitude <= 0.001f) return Vector3.zero;
        baseDir.Normalize();

        // 通常敵戦車っぽく広めに探索
        for (int angle = 0; angle <= 180; angle += 2)
        {
            Vector3 rightDir = Quaternion.Euler(0f, angle, 0f) * baseDir;
            if (SimulateRaycastTrajectory(startPos, rightDir, maxBounces, layerMask, 0, data))
                return rightDir;

            if (angle != 0)
            {
                Vector3 leftDir = Quaternion.Euler(0f, -angle, 0f) * baseDir;
                if (SimulateRaycastTrajectory(startPos, leftDir, maxBounces, layerMask, 0, data))
                    return leftDir;
            }
        }

        return Vector3.zero;
    }

    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce, EnemyData data)
    {
        if (currentBounce > 15) return false;
        dir.y = 0;
        dir.Normalize();

        float checkRadius = (data != null) ? data.raycastRadius : 0.25f;
        RaycastHit[] hits = Physics.SphereCastAll(startPos, checkRadius, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform) || (coreUnitRb != null && hit.collider.transform.IsChildOf(coreUnitRb.transform))) continue;
            if (hit.distance == 0) continue;

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
            {
                if (bouncesLeft > 0)
                {
                    Vector3 reflectDir = Vector3.Reflect(dir, hit.normal);
                    reflectDir.y = 0;
                    reflectDir.Normalize();
                    return SimulateRaycastTrajectory(hit.point + hit.normal * 0.05f, reflectDir, bouncesLeft - 1, layerMask, currentBounce + 1, data);
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

    private void IgnoreShellCollisionTemporarily(GameObject shellObj, float ignoreTime)
    {
        if (shellObj == null) return;

        Collider[] shellCols = shellObj.GetComponentsInChildren<Collider>();
        Collider[] mainCols = GetComponentsInChildren<Collider>();

        foreach (var sc in shellCols)
        {
            if (sc == null) continue;
            foreach (var mc in mainCols)
            {
                if (mc == null) continue;
                Physics.IgnoreCollision(sc, mc, true);
            }
        }

        StartCoroutine(RestoreShellCollisionRoutine(shellCols, mainCols, ignoreTime));
    }

    private IEnumerator RestoreShellCollisionRoutine(Collider[] shellCols, Collider[] mainCols, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (shellCols == null || mainCols == null) yield break;

        foreach (var sc in shellCols)
        {
            if (sc == null) continue;
            foreach (var mc in mainCols)
            {
                if (mc == null) continue;
                Physics.IgnoreCollision(sc, mc, false);
            }
        }
    }

    // ==========================================
    // 武装：火炎放射（本体のみ）
    // ==========================================
    private void CheckAndUseFlamethrower()
    {
        if (_isFlaming || _currentFlameCooldown > 0 || mainTurret == null || _currentTarget == null || flamethrowerPoint == null) return;

        float distToMain = Vector3.Distance(mainTurret.position, _currentTarget.transform.position);
        float distToCore = (coreTurret != null) ? Vector3.Distance(coreTurret.position, _currentTarget.transform.position) : 999f;

        if (distToMain <= flameDetectRadius || distToCore <= flameDetectRadius)
        {
            StartCoroutine(FlamethrowerRoutine());
        }
    }

    private IEnumerator FlamethrowerRoutine()
    {
        _isFlaming = true;
        _isSpinningMode = false;

        float timer = 0f;
        float fireInterval = 1f / flameFireRate;
        float nextFireTime = 0f;

        while (timer < flameDuration && !tankStatus.IsDead)
        {
            if (_currentTarget == null || _currentTarget.IsDead) break;

            float distToMain = Vector3.Distance(mainTurret.position, _currentTarget.transform.position);
            float distToCore = (coreTurret != null) ? Vector3.Distance(coreTurret.position, _currentTarget.transform.position) : 999f;

            if (distToMain > flameDetectRadius + 2.0f && distToCore > flameDetectRadius + 2.0f)
            {
                break;
            }

            timer += Time.deltaTime;

            if (timer >= nextFireTime && flameShellPrefab != null)
            {
                nextFireTime = timer + fireInterval;

                float randomWobble = Random.Range(-5f, 5f);
                Quaternion fireRotation = flamethrowerPoint.rotation * Quaternion.Euler(0, randomWobble, 0);

                GameObject shellObj = Instantiate(flameShellPrefab, flamethrowerPoint.position, fireRotation);
                if (shellObj.TryGetComponent(out ShellController shell)) shell.Launch(gameObject, 0);
            }

            yield return null;
        }

        _currentFlameCooldown = flameCooldown;
        _isFlaming = false;
        _isSpinningMode = true;
    }
}