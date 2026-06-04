using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Linq;

/// <summary>
/// パーティタンクの「コア（後衛）」を制御するコントローラー。
/// ただの敵戦車とほぼ同じ挙動をしつつ、スポーンボックスを配置します。
/// ダメージは本体に肩代わりさせます。
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(NavMeshAgent))]
public class PartyTankCoreController : MonoBehaviour
{
    [Header("基本設定")]
    [Tooltip("コア自身のTankStatus。必ずアタッチしてください。")]
    public TankStatus coreTankStatus;
    public EnemyData coreEnemyData;

    [Tooltip("紐付け先の本体（ダメージの送信先になります）")]
    public PartyTankBodyController ownerMain;

    [Header("武装・スポナー")]
    public Transform coreTurret;
    public Transform coreFirePoint;
    public Transform coreMuzzleFlashPoint;
    [Tooltip("発射口モデルの向き補正（度）。弾道と実弾がずれるときに調整")]
    public float coreAimYawOffset = 0f;

    [Tooltip("コアが投下するスポーンボックスのプレハブ")]
    public GameObject tankSpawnerBoxPrefab;
    public float boxPlacementInterval = 6.0f;
    public float boxPlacementDelay = 1.0f; // 設置時の硬直

    [Header("移動設定")]
    [Tooltip("敵から逃げるときの移動先までの距離")]
    public float fleeDistance = 8f;
    [Tooltip("逃げ先を更新する間隔（秒）")]
    public float fleeTargetRefreshInterval = 4f;
    [Tooltip("まっすぐ逃げる確率（0〜1）。0.7 = 7割直線逃げ・3割ランダム")]
    [Range(0f, 1f)]
    public float fleeDirectRatio = 0.7f;
    [Tooltip("ランダムが入るときの最大角度（度）")]
    public float fleeRandomAngleMax = 12f;
    [Tooltip("ターゲットなし時のランダム散策の半径")]
    public float wanderRadius = 8f;

    [Header("回避設定")]
    [Tooltip("壁を避ける半径")]
    public float wallAvoidRadius = 5.0f;
    [Tooltip("隅判定の壁プローブ距離")]
    public float wallCornerProbeRadius = 3f;
    [Tooltip("移動先が壁から離れている必要がある距離")]
    public float destinationWallClearance = 2.5f;
    [Tooltip("壁・隅付近で逃げるときのランダム角度（度）")]
    public float nearWallFleeRandomAngleMax = 45f;
    [Tooltip("ステージ端から内側に保ちたい距離（四隅寄りを減らす）")]
    public float stageEdgeMargin = 4f;
    [Tooltip("逃げ方向にステージ中央を混ぜる割合（0〜1）")]
    [Range(0f, 1f)]
    public float fleeCenterBias = 0.4f;
    [Tooltip("移動先更新時にステージ全体から目的地を選ぶ確率")]
    [Range(0f, 1f)]
    public float stageWanderChance = 0.45f;

    private const float StageLimit = 13.5f;

    // 内部変数
    private Rigidbody _rb;
    private NavMeshAgent _agent;
    private LineRenderer _lineRenderer;
    private TankStatus _currentTarget;

    private Vector3 _moveTarget;
    private float _moveTimer;
    private float _stuckTimer;
    private Vector3 _smoothedMoveDir;

    private float _currentFireCooldown;
    private float _boxPlacementTimer;
    private int _currentAmmoCount;
    private float _shotRigidTimer;

    private Vector3 _smartAimDir = Vector3.zero;
    private float _smartAimTimer = 0f;
    private float _wallWanderNoiseTime;
    private int _obstacleLayerMask;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _agent = GetComponent<NavMeshAgent>();
        _lineRenderer = GetComponent<LineRenderer>();
        _obstacleLayerMask = LayerMask.GetMask("Wall", "Spike");
        _wallWanderNoiseTime = Random.Range(0f, 100f);
        if (_lineRenderer == null) _lineRenderer = gameObject.AddComponent<LineRenderer>();
        _lineRenderer.enabled = false;
        _lineRenderer.startWidth = 0.1f;
        _lineRenderer.endWidth = 0.1f;

        if (coreTankStatus == null) coreTankStatus = GetComponent<TankStatus>();

        // ★追加: クラス名変更等でインスペクターの参照が外れていた場合の自動取得
        if (ownerMain == null)
        {
            ownerMain = transform.parent != null ? transform.parent.GetComponentInChildren<PartyTankBodyController>() : GetComponentInParent<PartyTankBodyController>();
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

        // ★修正: 以前ここでコアのHPを強制的に1にしていたバグ記述を削除しました。
        // （これによりコアが死んで消滅してしまうことを防ぎます）

        // コア全体を弱点として設定し、ダメージを本体へ流す
        SetupWeakPoint();

        if (coreTankStatus != null)
        {
            _currentAmmoCount = coreTankStatus.GetTotalMaxAmmo();

            // HPバーを非表示にするため、HPBarManagerから登録解除する
            if (HPBarManager.Instance != null)
            {
                HPBarManager.Instance.UnregisterTank(coreTankStatus);
            }

            // 自身のTankStatusが直接ダメージを受けた場合も本体に肩代わりさせる
            if (ownerMain != null && ownerMain.mainTankStatus != null)
            {
                coreTankStatus.damageForwardTarget = ownerMain.mainTankStatus;
                coreTankStatus.SetTeam(ownerMain.mainTankStatus.team); // 初期化時にもチームを同期
            }
        }

        _boxPlacementTimer = boxPlacementInterval;

        DecideNextMoveTarget();
    }

    /// <summary>
    /// 自身のコライダーにWeakPointを追加し、本体（ownerMain）のTankStatusにダメージを転送する
    /// </summary>
    private void SetupWeakPoint()
    {
        if (ownerMain == null || ownerMain.mainTankStatus == null) return;

        Collider[] cols = GetComponentsInChildren<Collider>();
        foreach (Collider c in cols)
        {
            if (c.isTrigger) continue;

            if (c.gameObject.GetComponent<WeakPoint>() == null)
            {
                WeakPoint wp = c.gameObject.AddComponent<WeakPoint>();
                // WeakPointのダメージ送信先を本体のTankStatusに設定
                wp.bossStatus = ownerMain.mainTankStatus;
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

    private void Update()
    {
        // ★修正: ゲーム開始前、終了時は処理を止める（フライング防止）
        if (GameManager.Instance == null || !GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished())
        {
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            return;
        }

        // 本体が死んだらコアも機能停止
        if (ownerMain == null || ownerMain.mainTankStatus == null || ownerMain.mainTankStatus.IsDead)
        {
            if (_agent != null && _agent.enabled) _agent.isStopped = true;
            if (_lineRenderer != null) _lineRenderer.enabled = false;

            // 本体が死んだらコアも強制的に破壊（クリア演出のため）
            if (coreTankStatus != null && !coreTankStatus.IsDead)
            {
                coreTankStatus.damageForwardTarget = null; // 転送を解除してから自壊
                coreTankStatus.TakeDamage(99999);
            }
            return;
        }

        // ★追加: 本体のチームと常に同期させる
        if (coreTankStatus != null && ownerMain.mainTankStatus != null && coreTankStatus.team != ownerMain.mainTankStatus.team)
        {
            coreTankStatus.SetTeam(ownerMain.mainTankStatus.team);
        }

        ThinkTarget();
        ThinkMoveLogic();
        HandleTurretLogic();
        HandleSpawnBoxPlacement();

        if (_currentFireCooldown > 0) _currentFireCooldown -= Time.deltaTime;
        if (_shotRigidTimer > 0f) _shotRigidTimer -= Time.deltaTime;
    }

    private void FixedUpdate()
    {
        // ★修正: ゲーム開始前、終了時は物理移動を止める
        if (GameManager.Instance == null || !GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished())
        {
            if (_rb != null) _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            return;
        }

        if (ownerMain == null || ownerMain.mainTankStatus == null || ownerMain.mainTankStatus.IsDead)
        {
            if (_rb != null) _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
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
        if (coreTankStatus == null || coreTankStatus.IsDead) return;

        if (DebugVisualizer.Instance != null && _lineRenderer != null && coreFirePoint != null && coreTankStatus != null)
        {
            GameObject shellToUse = coreTankStatus.GetShellPrefab();
            int bounces = coreTankStatus.GetRicochetCountForPrefab(shellToUse);
            if (coreEnemyData != null && !coreEnemyData.considerReflection) bounces = 0;

            Vector3 aimDir = GetCoreMuzzleDirection();
            DebugVisualizer.Instance.DrawTrajectoryLine(_lineRenderer, coreFirePoint.position, aimDir, bounces);
        }
    }

    private void ThinkTarget()
    {
        _currentTarget = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            .Where(t => t != null && !t.IsDead && coreTankStatus != null && t.team != coreTankStatus.team && t != ownerMain.mainTankStatus)
            .OrderBy(t => Vector3.Distance(transform.position, t.transform.position))
            .FirstOrDefault();
    }

    private void ThinkMoveLogic()
    {
        if (coreEnemyData == null || _agent == null) return;
        _moveTimer += Time.deltaTime;

        if (_agent.isOnNavMesh)
        {
            Vector3 finalDest = _moveTarget;

            // 臆病（Coward）：敵から離れる（ランダム要素は控えめ）
            if (coreEnemyData.aiType == EnemyData.AIType.Coward && _currentTarget != null)
            {
                bool nearWallOrCorner = IsNearWall(wallCornerProbeRadius + 1f) || IsInWallCorner(wallCornerProbeRadius);
                bool needRefresh = _moveTimer > fleeTargetRefreshInterval
                    || Vector3.Distance(transform.position, _moveTarget) < 2.5f
                    || (_agent.hasPath && _agent.remainingDistance < 2.5f)
                    || nearWallOrCorner;

                if (needRefresh)
                {
                    _moveTimer = 0f;
                    bool preferStageWander = nearWallOrCorner || Random.value < stageWanderChance;
                    if (!preferStageWander || !TryPickRandomStageDestination())
                        if (!TrySetFleeDestination())
                            TryPickRandomStageDestination();
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

    private bool TrySetFleeDestination()
    {
        if (_currentTarget == null) return false;

        Vector3 awayDir = transform.position - _currentTarget.transform.position;
        awayDir.y = 0f;
        if (awayDir.sqrMagnitude < 0.01f) awayDir = transform.forward;
        awayDir.Normalize();

        bool nearWall = IsNearWall(wallCornerProbeRadius + 1f);
        bool inCorner = IsInWallCorner(wallCornerProbeRadius);

        awayDir = BlendFleeDirection(awayDir);

        if (inCorner)
        {
            Vector3 escape = GetCornerEscapeVector(wallCornerProbeRadius);
            if (escape.sqrMagnitude > 0.001f) awayDir = escape.normalized;
        }
        else if (nearWall || Random.value > fleeDirectRatio)
        {
            float maxAngle = nearWall ? nearWallFleeRandomAngleMax : fleeRandomAngleMax;
            awayDir = (Quaternion.Euler(0f, Random.Range(-maxAngle, maxAngle), 0f) * awayDir).normalized;
        }

        if (TryPickNavMeshDestination(awayDir, fleeDistance, 8)) return true;
        return TryPickRandomStageDestination();
    }

    private Vector3 BlendFleeDirection(Vector3 awayFromEnemy)
    {
        awayFromEnemy.y = 0f;
        if (awayFromEnemy.sqrMagnitude < 0.001f) awayFromEnemy = transform.forward;
        awayFromEnemy.Normalize();

        Vector3 toCenter = new Vector3(-transform.position.x, 0f, -transform.position.z);
        float centerWeight = fleeCenterBias;
        if (Mathf.Abs(transform.position.x) > StageLimit - stageEdgeMargin
            || Mathf.Abs(transform.position.z) > StageLimit - stageEdgeMargin)
            centerWeight = Mathf.Max(centerWeight, 0.55f);
        if (IsInWallCorner(wallCornerProbeRadius))
            centerWeight = Mathf.Max(centerWeight, 0.75f);

        if (toCenter.sqrMagnitude < 1f) return awayFromEnemy;
        toCenter.Normalize();
        return (awayFromEnemy * (1f - centerWeight) + toCenter * centerWeight).normalized;
    }

    private Vector3 GetRandomStagePoint()
    {
        float inner = Mathf.Max(1f, StageLimit - stageEdgeMargin);
        return new Vector3(Random.Range(-inner, inner), 0f, Random.Range(-inner, inner));
    }

    private bool TryPickRandomStageDestination()
    {
        Vector3 bestPos = Vector3.zero;
        float bestScore = float.MinValue;

        for (int i = 0; i < 14; i++)
        {
            Vector3 stagePoint = GetRandomStagePoint();
            if (!NavMesh.SamplePosition(stagePoint, out NavMeshHit hit, 14f, NavMesh.AllAreas)) continue;
            if (Vector3.Distance(transform.position, hit.position) < 4f) continue;

            float score = ScoreDestination(hit.position);
            if (score > bestScore)
            {
                bestScore = score;
                bestPos = hit.position;
            }
        }

        if (bestScore > float.MinValue)
        {
            _moveTarget = bestPos;
            return true;
        }
        return false;
    }

    private void DecideNextMoveTarget()
    {
        _moveTimer = 0f;

        if (TryPickRandomStageDestination()) return;

        if (coreEnemyData != null && coreEnemyData.aiType == EnemyData.AIType.Coward && _currentTarget != null)
        {
            if (TrySetFleeDestination()) return;
        }

        Vector3 preferredDir = transform.forward;
        if (IsInWallCorner(wallCornerProbeRadius))
        {
            Vector3 escape = GetCornerEscapeVector(wallCornerProbeRadius);
            if (escape.sqrMagnitude > 0.001f) preferredDir = escape.normalized;
        }
        else if (IsNearWall(wallCornerProbeRadius))
        {
            preferredDir = (Quaternion.Euler(0f, Random.Range(-120f, 120f), 0f) * transform.forward).normalized;
        }

        if (TryPickNavMeshDestination(preferredDir, wanderRadius, 8)) return;
        if (TryPickRandomStageDestination()) return;

        _moveTarget = transform.position;
    }

    private bool TryPickNavMeshDestination(Vector3 preferredDir, float distance, int attemptCount)
    {
        preferredDir.y = 0f;
        if (preferredDir.sqrMagnitude < 0.001f) preferredDir = transform.forward;
        preferredDir.Normalize();

        Vector3 bestPos = Vector3.zero;
        float bestScore = float.MinValue;

        for (int i = 0; i < attemptCount; i++)
        {
            Vector3 candidate;
            if (i == 0)
            {
                candidate = transform.position + preferredDir * distance;
            }
            else if (i % 3 == 0)
            {
                candidate = GetRandomStagePoint();
            }
            else
            {
                float angle = Random.Range(-nearWallFleeRandomAngleMax, nearWallFleeRandomAngleMax);
                Vector3 dir = (Quaternion.Euler(0f, angle, 0f) * preferredDir).normalized;
                candidate = transform.position + dir * distance;
            }

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 12f, NavMesh.AllAreas)) continue;
            if (Vector3.Distance(transform.position, hit.position) < 2f) continue;

            float score = ScoreDestination(hit.position);
            if (score > bestScore)
            {
                bestScore = score;
                bestPos = hit.position;
            }
        }

        if (bestScore > float.MinValue)
        {
            _moveTarget = bestPos;
            return true;
        }
        return false;
    }

    private float ScoreDestination(Vector3 pos)
    {
        if (!IsDestinationClear(pos, destinationWallClearance)) return -100f;
        if (IsPositionInCorner(pos, wallCornerProbeRadius)) return -100f;

        float score = 0f;
        Vector3 origin = pos + Vector3.up * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, destinationWallClearance * 2f, _obstacleLayerMask))
                score -= (1f - hit.distance / (destinationWallClearance * 2f)) * 2f;
        }

        float edgeClearX = StageLimit - Mathf.Abs(pos.x);
        float edgeClearZ = StageLimit - Mathf.Abs(pos.z);
        float edgeClear = Mathf.Min(edgeClearX, edgeClearZ);
        score += edgeClear * 0.85f;
        if (edgeClear < stageEdgeMargin) score -= (stageEdgeMargin - edgeClear) * 3f;

        float distFromCenter = new Vector2(pos.x, pos.z).magnitude;
        score += Mathf.Clamp(StageLimit - distFromCenter, 0f, StageLimit) * 0.12f;

        score += Mathf.Min(Vector3.Distance(transform.position, pos), fleeDistance) * 0.12f;
        score += Random.Range(0f, 0.35f);
        return score;
    }

    private bool IsPositionInCorner(Vector3 worldPos, float probeDist)
    {
        Vector3 origin = worldPos + Vector3.up * 0.5f;
        int closeHits = 0;
        Vector3 normalSum = Vector3.zero;

        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, probeDist, _obstacleLayerMask)
                && hit.distance < probeDist * 0.9f)
            {
                closeHits++;
                normalSum += hit.normal;
            }
        }

        return closeHits >= 3 || (closeHits >= 2 && normalSum.magnitude > 1.1f);
    }

    private bool IsDestinationClear(Vector3 pos, float clearance)
    {
        Vector3 origin = pos + Vector3.up * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (Physics.Raycast(origin, dir, clearance, _obstacleLayerMask)) return false;
        }
        return true;
    }

    private bool IsNearWall(float probeDist)
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (Physics.Raycast(origin, dir, probeDist, _obstacleLayerMask)) return true;
        }
        return false;
    }

    private bool IsInWallCorner(float probeDist) => IsPositionInCorner(transform.position, probeDist);

    private Vector3 GetCornerEscapeVector(float probeDist)
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Vector3 push = Vector3.zero;

        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, probeDist, _obstacleLayerMask))
            {
                float strength = 1f - (hit.distance / probeDist);
                push += hit.normal * strength;
            }
        }

        push.y = 0f;
        return push;
    }

    private void ExecuteMovement()
    {
        if ((_shotRigidTimer > 0f) && !coreTankStatus.isDevilBerserk)
        {
            if (_rb != null) _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
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

        bool inCorner = IsInWallCorner(wallCornerProbeRadius);
        bool nearWall = IsNearWall(wallCornerProbeRadius + 0.5f);

        Vector3 wallAvoid = GetWallAvoidanceVector(wallAvoidRadius);
        if (wallAvoid != Vector3.zero)
        {
            float pathWeight = (inCorner || nearWall) ? 0.05f : 0.2f;
            float wallWeight = inCorner ? 8f : (nearWall ? 6.5f : 5f);
            finalDir = (finalDir * pathWeight + wallAvoid * wallWeight).normalized;
        }

        if (inCorner)
        {
            Vector3 cornerEscape = GetCornerEscapeVector(wallCornerProbeRadius);
            if (cornerEscape.sqrMagnitude > 0.001f)
                finalDir = (finalDir * 0.15f + cornerEscape.normalized * 4f).normalized;
        }

        Vector3 toStageCenter = new Vector3(-transform.position.x, 0f, -transform.position.z);
        if (toStageCenter.sqrMagnitude > 1f)
        {
            float edgePush = 0f;
            float edgeX = StageLimit - Mathf.Abs(transform.position.x);
            float edgeZ = StageLimit - Mathf.Abs(transform.position.z);
            if (edgeX < stageEdgeMargin) edgePush += (stageEdgeMargin - edgeX) * 0.25f;
            if (edgeZ < stageEdgeMargin) edgePush += (stageEdgeMargin - edgeZ) * 0.25f;
            if (inCorner) edgePush = Mathf.Max(edgePush, 0.6f);
            if (edgePush > 0f)
                finalDir = (finalDir + toStageCenter.normalized * edgePush).normalized;
        }

        if (nearWall || inCorner)
        {
            _wallWanderNoiseTime += Time.fixedDeltaTime * 0.7f;
            float noise = Mathf.PerlinNoise(_wallWanderNoiseTime, 0.5f) * 2f - 1f;
            Vector3 jitter = Quaternion.Euler(0f, noise * 55f, 0f) * finalDir;
            finalDir = (finalDir + jitter * 0.35f).normalized;
        }

        if (Physics.SphereCast(transform.position + Vector3.up * 0.5f, 0.6f, finalDir.normalized, out RaycastHit sphereHit, 1.5f, _obstacleLayerMask))
        {
            Vector3 wallNormal = sphereHit.normal;
            wallNormal.y = 0;
            Vector3 slideVel = Vector3.ProjectOnPlane(finalDir, wallNormal);
            finalDir = slideVel.magnitude < 0.1f ? wallNormal : slideVel.normalized + wallNormal * 0.5f;
        }

        _smoothedMoveDir = Vector3.Lerp(_smoothedMoveDir == Vector3.zero ? transform.forward : _smoothedMoveDir, finalDir.normalized, Time.fixedDeltaTime * 6.0f).normalized;

        float targetAngle = Mathf.Atan2(_smoothedMoveDir.x, _smoothedMoveDir.z) * Mathf.Rad2Deg;
        float currentY = _rb.rotation.eulerAngles.y;
        float rotSpeed = coreTankStatus != null ? coreTankStatus.GetCurrentRotationSpeed() : 120f;

        float nextAngle = Mathf.MoveTowardsAngle(currentY, targetAngle, rotSpeed * Time.fixedDeltaTime);
        _rb.MoveRotation(Quaternion.Euler(0f, nextAngle, 0f));

        float angleDiff = Mathf.Abs(Mathf.DeltaAngle(currentY, targetAngle));
        float moveScale = angleDiff > 90f ? 0f : (angleDiff > 45f ? 0.35f : (angleDiff > 20f ? 0.7f : 1f));
        if (coreTankStatus.isDevilBerserk) moveScale = 1f; // 暴走中は常に前進

        float speed = coreTankStatus != null ? coreTankStatus.GetCurrentMoveSpeed() : 5f;
        Vector3 vel = (Quaternion.Euler(0f, nextAngle, 0f) * Vector3.forward) * (speed * moveScale);
        _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);

        if (new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z).magnitude < 0.15f)
        {
            _stuckTimer += Time.fixedDeltaTime;
            float stuckLimit = (inCorner || nearWall) ? 0.6f : 1.0f;
            if (_stuckTimer > stuckLimit)
            {
                DecideNextMoveTarget();
                _stuckTimer = 0f;
            }
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
        if (coreEnemyData != null)
        {
            maxSearchRadius = Mathf.Max(maxSearchRadius, coreEnemyData.shellAvoidRadius, coreEnemyData.mineAvoidRadius, coreEnemyData.allyMineAvoidRadius);
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
                    float avoidRad = (coreEnemyData != null) ? coreEnemyData.shellAvoidRadius : 3.0f;
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
                    if (coreEnemyData != null)
                    {
                        avoidRad = (mineTeam == coreTankStatus.team) ? coreEnemyData.allyMineAvoidRadius : coreEnemyData.mineAvoidRadius;
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
        Vector3 origin = transform.position + Vector3.up * 0.5f;

        for (int i = 0; i < 16; i++)
        {
            float angle = i * 22.5f;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            float checkDist = (Mathf.Abs(angle) > 135f || Mathf.Abs(angle) < 45f) ? maxDist : maxDist * 0.75f;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, checkDist, _obstacleLayerMask))
            {
                float strength = 1.0f - (hit.distance / checkDist);
                avoidVec += hit.normal * strength;
            }
        }
        return avoidVec;
    }

    private void HandleTurretLogic()
    {
        if (coreTurret == null) return;

        Vector3 targetDir = transform.forward;
        if (_currentTarget != null)
        {
            targetDir = (_currentTarget.transform.position - coreTurret.position);
            targetDir.y = 0f;
            if (targetDir.sqrMagnitude > 0.001f) targetDir.Normalize();
            else targetDir = transform.forward;

            if (coreEnemyData != null && coreEnemyData.useSmartRicochet)
            {
                _smartAimTimer -= Time.deltaTime;
                if (_smartAimTimer <= 0f)
                {
                    _smartAimDir = FindSmartRicochetDirection();
                    _smartAimTimer = 0.1f;
                }

                if (_smartAimDir.sqrMagnitude > 0.001f) targetDir = _smartAimDir.normalized;
            }
        }

        if (targetDir.sqrMagnitude > 0.001f)
        {
            float rotSpeed = coreEnemyData != null ? coreEnemyData.turretRotationSpeed : 120f;
            ApplyCoreTurretAim(targetDir, rotSpeed * Time.deltaTime, false);
        }

        if (_currentTarget == null) return;

        if (CheckShootTrajectory() && _currentFireCooldown <= 0 && _currentAmmoCount > 0)
        {
            TryFireCore();
        }
    }

    private Vector3 GetCoreMuzzleDirection()
    {
        if (coreFirePoint == null) return transform.forward;
        Vector3 dir = coreFirePoint.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return transform.forward;
        return dir.normalized;
    }

    private void ApplyCoreTurretAim(Vector3 worldDir, float maxDegreesDelta, bool instant)
    {
        if (coreFirePoint == null) return;

        worldDir.y = 0f;
        if (worldDir.sqrMagnitude < 0.001f) return;
        worldDir.Normalize();

        Quaternion aimWorld = Quaternion.LookRotation(worldDir, Vector3.up) * Quaternion.Euler(0f, coreAimYawOffset, 0f);

        if (coreTurret != null && coreFirePoint.IsChildOf(coreTurret))
        {
            Quaternion turretWorld = aimWorld * Quaternion.Inverse(coreFirePoint.localRotation);
            float targetYaw = turretWorld.eulerAngles.y;
            if (instant || maxDegreesDelta < 0f)
                coreTurret.rotation = Quaternion.Euler(0f, targetYaw, 0f);
            else
                coreTurret.rotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(coreTurret.eulerAngles.y, targetYaw, maxDegreesDelta), 0f);
        }
        else if (coreTurret != null)
        {
            if (instant || maxDegreesDelta < 0f)
                coreTurret.rotation = aimWorld;
            else
                coreTurret.rotation = Quaternion.RotateTowards(coreTurret.rotation, aimWorld, maxDegreesDelta);
        }
        else
        {
            if (instant || maxDegreesDelta < 0f)
                coreFirePoint.rotation = aimWorld;
            else
                coreFirePoint.rotation = Quaternion.RotateTowards(coreFirePoint.rotation, aimWorld, maxDegreesDelta);
        }
    }

    private const float NearTurretBlockRadius = 2f;

    private bool IsFriendlyOrPartyNearTurret()
    {
        if (coreTankStatus == null) return false;

        Vector3 center = coreTurret != null ? coreTurret.position : (coreFirePoint != null ? coreFirePoint.position : transform.position);
        Collider[] closeHits = Physics.OverlapSphere(center, NearTurretBlockRadius);
        foreach (var col in closeHits)
        {
            if (col.transform.IsChildOf(transform)) continue;
            if (ownerMain != null && col.transform.IsChildOf(ownerMain.transform)) return true;

            if (col.GetComponentInParent<TankSpawnerBox>() != null)
            {
                TankStatus spawnerStatus = col.GetComponentInParent<TankStatus>();
                if (spawnerStatus == null || spawnerStatus.team == coreTankStatus.team) return true;
            }

            TankStatus ts = col.GetComponentInParent<TankStatus>();
            if (ts == null || ts.IsDead || ts == coreTankStatus) continue;
            if (ownerMain != null && ownerMain.mainTankStatus != null && ts == ownerMain.mainTankStatus) return true;
            if (ts.team == coreTankStatus.team) return true;
        }
        return false;
    }

    private bool CheckShootTrajectory()
    {
        if (coreFirePoint == null || coreTankStatus == null) return false;
        if (IsFriendlyOrPartyNearTurret()) return false;

        Vector3 startPos = coreFirePoint.position;
        Vector3 dir = GetCoreMuzzleDirection();
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");

        GameObject shellPrefab = coreTankStatus.GetShellPrefab();
        int maxBounces = coreTankStatus.GetRicochetCountForPrefab(shellPrefab);
        if (coreEnemyData != null && !coreEnemyData.considerReflection) maxBounces = 0;

        if (coreEnemyData != null && coreEnemyData.useSmartRicochet && _smartAimDir.sqrMagnitude > 0.001f)
        {
            Vector3 smartDir = _smartAimDir;
            smartDir.y = 0f;
            smartDir.Normalize();

            if (Vector3.Angle(dir, smartDir) > coreEnemyData.shotAllowAngle) return false;

            if (!SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0, coreEnemyData))
            {
                _smartAimDir = Vector3.zero;
                _smartAimTimer = 0f;
                return false;
            }

            ApplyCoreTurretAim(smartDir, -1f, true);
            return true;
        }

        return SimulateRaycastTrajectory(startPos, dir, maxBounces, layerMask, 0, coreEnemyData);
    }

    private Vector3 FindSmartRicochetDirection()
    {
        if (coreFirePoint == null || _currentTarget == null || coreTankStatus == null) return Vector3.zero;

        int maxBounces = coreTankStatus.GetRicochetCountForPrefab(coreTankStatus.GetShellPrefab());
        if (maxBounces <= 0 || coreEnemyData == null || !coreEnemyData.considerReflection) return Vector3.zero;

        Vector3 startPos = coreFirePoint.position;
        int layerMask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Spike", "Mine", "Ignore Raycast");
        Vector3 baseDir = _currentTarget.transform.position - startPos;
        baseDir.y = 0f;
        if (baseDir.sqrMagnitude < 0.001f) baseDir = transform.forward;
        else baseDir.Normalize();

        for (int angle = 0; angle <= 180; angle += 3)
        {
            Vector3 rightDir = Quaternion.Euler(0f, angle, 0f) * baseDir;
            if (SimulateRaycastTrajectory(startPos, rightDir, maxBounces, layerMask, 0, coreEnemyData)) return rightDir;

            if (angle != 0 && angle != 180)
            {
                Vector3 leftDir = Quaternion.Euler(0f, -angle, 0f) * baseDir;
                if (SimulateRaycastTrajectory(startPos, leftDir, maxBounces, layerMask, 0, coreEnemyData)) return leftDir;
            }
        }
        return Vector3.zero;
    }

    public void OnMaxAmmoIncreased()
    {
        _currentAmmoCount = coreTankStatus.GetTotalMaxAmmo();
    }

    private void TryFireCore()
    {
        if (IsFriendlyOrPartyNearTurret()) return;

        // ★修正: 壁のめり込み検知（EnemyTankControllerと同じ処理）
        int wallLayerMask = LayerMask.GetMask("Wall");
        Vector3 turretCenter = coreTurret != null ? coreTurret.position : transform.position;
        float checkRadius = coreEnemyData != null ? coreEnemyData.raycastRadius : 0.25f;

        if (Physics.CheckSphere(coreFirePoint.position, checkRadius, wallLayerMask)) return;
        if (Physics.Linecast(turretCenter, coreFirePoint.position, wallLayerMask)) return;

        _currentFireCooldown = coreEnemyData != null ? coreEnemyData.fireCooldown : 1.5f;
        _currentAmmoCount--;
        StartCoroutine(ReloadCoreAmmoRoutine());

        if (EffectManager.Instance != null)
        {
            EffectManager.Instance.ShotSound();
            EffectManager.Instance.PlayMuzzleFlash(coreMuzzleFlashPoint != null ? coreMuzzleFlashPoint : coreFirePoint);
        }

        GameObject shellPrefab = coreTankStatus != null ? coreTankStatus.GetShellPrefab() : null;
        if (shellPrefab != null && coreFirePoint != null)
        {
            GameObject shellObj = Instantiate(shellPrefab, coreFirePoint.position, coreFirePoint.rotation);
            if (shellObj.TryGetComponent(out ShellController shell)) shell.Launch(gameObject, 0);
        }

        _shotRigidTimer = coreTankStatus != null && coreTankStatus.GetData() != null ? coreTankStatus.GetData().shotDelay : 0.2f;
    }

    private IEnumerator ReloadCoreAmmoRoutine()
    {
        float cooldown = coreTankStatus != null && coreTankStatus.GetData() != null ? coreTankStatus.GetData().ammoCooldown : 1.5f;
        yield return new WaitForSeconds(cooldown);

        int maxAmmo = coreTankStatus != null ? coreTankStatus.GetTotalMaxAmmo() : 5;
        if (_currentAmmoCount < maxAmmo) _currentAmmoCount++;
    }

    private bool SimulateRaycastTrajectory(Vector3 startPos, Vector3 dir, int bouncesLeft, int layerMask, int currentBounce, EnemyData data)
    {
        if (currentBounce > 15) return false;
        dir.y = 0; dir.Normalize();

        float radius = data != null ? data.raycastRadius : 0.25f;
        RaycastHit[] hits = Physics.SphereCastAll(startPos, radius, dir, 100f, layerMask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            TankStatus hitTank = hit.collider.GetComponentInParent<TankStatus>();
            if (hitTank != null && (hitTank == coreTankStatus || (ownerMain != null && hitTank == ownerMain.mainTankStatus))) continue;

            if (hit.distance == 0) continue;

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Wall") || hit.collider.CompareTag("Wall"))
            {
                if (bouncesLeft <= 0) return false;
                Vector3 r = Vector3.Reflect(dir, hit.normal);
                r.y = 0;
                r.Normalize();

                return SimulateRaycastTrajectory(hit.point + hit.normal * 0.05f, r, bouncesLeft - 1, layerMask, currentBounce + 1, data);
            }

            if (hitTank != null && coreTankStatus != null) return hitTank.team != coreTankStatus.team;
        }
        return false;
    }

    private void HandleSpawnBoxPlacement()
    {
        if (tankSpawnerBoxPrefab == null) return;

        _boxPlacementTimer -= Time.deltaTime;
        if (_boxPlacementTimer <= 0f)
        {
            _boxPlacementTimer = boxPlacementInterval;

            if (!Physics.OverlapSphere(transform.position, 2.0f).Any(c => c.CompareTag("Mine")))
            {
                // 地面への埋まりなどを防ぐため現在座標から直接配置
                GameObject box = Instantiate(tankSpawnerBoxPrefab, transform.position, Quaternion.identity);
                if (box.TryGetComponent(out TankSpawnerBox spawnerBox) && coreTankStatus != null)
                {
                    spawnerBox.Init(coreTankStatus, coreTankStatus.team);
                }

                _shotRigidTimer = Mathf.Max(_shotRigidTimer, boxPlacementDelay);
            }
        }
    }
}