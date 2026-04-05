using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
public class TankController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TankStatus tankStatus;
    [SerializeField] private Transform turretTransform;
    [SerializeField] private Transform firePoint;

    [Header("Mine Settings")]
    [Tooltip("地雷を設置する位置のZ軸のズレ（マイナスで戦車の後方、0で戦車の中心）")]
    [SerializeField] private float mineSpawnOffsetZ = 2f; // ← この数値をインスペクターで0などにすれば中心に置けます

    private Rigidbody _rb;
    private Vector2 _moveInput;
    private Vector2 _aimInput;
    private bool _isUsingGamepad;
    private int _currentAmmoCount;
    private float _targetAngle;
    private bool _isReverse;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void Start()
    {
        // ゲーム開始時に最大弾数（初期値）をセット
        _currentAmmoCount = tankStatus.GetTotalMaxAmmo();
    }

    // ★修正: ゲームが「開始されている」かつ「終了していない」時だけ入力を受け付ける
    private bool IsGameActive => GameManager.Instance == null || (GameManager.Instance.IsGameStarted && !GameManager.Instance.IsGameFinished());

    public void OnMove(InputAction.CallbackContext context)
    {
        // ★修正: ゲーム開始前でも入力「値」自体は常に受け取って保存しておく（弾かない）
        _moveInput = context.ReadValue<Vector2>();

        // 値が入力されていれば目標の角度だけは計算しておく
        if (_moveInput.sqrMagnitude > 0.01f) UpdateTargetDirection();
    }

    public void OnAim(InputAction.CallbackContext context)
    {
        if (!IsGameActive) { _aimInput = Vector2.zero; return; }
        _aimInput = context.ReadValue<Vector2>();
        _isUsingGamepad = context.control.device is not Pointer;
    }

    public void OnFire(InputAction.CallbackContext context)
    {
        if (!IsGameActive) return;
        if (context.started && _currentAmmoCount > 0 && !tankStatus.IsInStun)
        {
            StartCoroutine(ShootRoutine());
        }
    }

    [Tooltip("地雷設置ボタンを押した際の処理")]
    public void OnMine(InputAction.CallbackContext context)
    {
        if (!IsGameActive) return;

        // ボタンが押された瞬間かどうか、スタン中ではないかを判定
        if (!context.performed || tankStatus.IsInStun) return;

        // 設置上限チェック（制限以上の場合は設置できないようにする）
        if (tankStatus.ActiveMineCount >= tankStatus.GetTotalMineLimit()) return;

        // ★修正: スポーンボックス専用ではなく、共通の地雷設置ルーチンを呼ぶように変更
        StartCoroutine(PlaceMineRoutine());
    }

    private void Update()
    {
        if (!IsGameActive) return;
        HandleTurretRotation();
    }

    private void FixedUpdate()
    {
        if (!IsGameActive)
        {
            _rb.linearVelocity = Vector3.zero;
            return;
        }

        if (_rb.isKinematic) return;

        // ★硬直中は物理移動を完全に止める
        if (tankStatus.IsInStun)
        {
            _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            return;
        }

        if (_moveInput.sqrMagnitude > 0.01f) HandleMovement();
        else _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, new Vector3(0, _rb.linearVelocity.y, 0), 0.2f);
    }

    private void HandleTurretRotation()
    {
        if (turretTransform == null) return;
        Vector3 targetDirection = Vector3.zero;
        if (_isUsingGamepad) { if (_aimInput.sqrMagnitude > 0.1f) targetDirection = new Vector3(_aimInput.x, 0, _aimInput.y); }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(_aimInput);
            Plane groundPlane = new Plane(Vector3.up, turretTransform.position);
            if (groundPlane.Raycast(ray, out float distance)) targetDirection = ray.GetPoint(distance) - turretTransform.position;
        }
        if (targetDirection != Vector3.zero) { targetDirection.y = 0; turretTransform.rotation = Quaternion.LookRotation(targetDirection); }
    }

    private void UpdateTargetDirection()
    {
        float inputAngle = Mathf.Atan2(_moveInput.x, _moveInput.y) * Mathf.Rad2Deg;
        float angleDiff = Mathf.DeltaAngle(transform.eulerAngles.y, inputAngle);
        if (Mathf.Abs(angleDiff) > 90f) { _targetAngle = Mathf.Repeat(inputAngle + 180f, 360f); _isReverse = true; }
        else { _targetAngle = inputAngle; _isReverse = false; }
    }

    private void HandleMovement()
    {
        var data = tankStatus.GetData();
        float currentAngle = _rb.rotation.eulerAngles.y;

        // ★変更: GetData().rotationSpeed ではなく GetCurrentRotationSpeed() を使用
        float rotationSpeed = tankStatus.GetCurrentRotationSpeed();

        float nextAngle = Mathf.MoveTowardsAngle(currentAngle, _targetAngle, rotationSpeed * Time.fixedDeltaTime);
        _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

        if (Mathf.Abs(Mathf.DeltaAngle(nextAngle, _targetAngle)) < 2.0f)
        {
            float currentSpeed = tankStatus.GetCurrentMoveSpeed() * (_isReverse ? -1f : 1f);
            Vector3 vel = transform.forward * currentSpeed;
            _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
        }
        else _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
    }

    private IEnumerator ShootRoutine()
    {
        _currentAmmoCount--;

        // ★修正: ApplyStunを使用してタイマーを設定する (Updateでの即時解除を防ぐ)
        float delay = tankStatus.GetData().shotDelay;
        tankStatus.ApplyStun(delay);

        if (EffectManager.Instance != null && firePoint != null) EffectManager.Instance.PlayMuzzleFlash(firePoint);
        if (tankStatus.GetShellPrefab() != null)
        {
            EffectManager.Instance.ShotSound();
            GameObject shellObj = Instantiate(tankStatus.GetShellPrefab(), firePoint.position, firePoint.rotation);
            // ShellControllerがOwnerを参照するので、ボーナス値は自動的に加算される
            shellObj.GetComponent<ShellController>()?.Launch(this.gameObject, 0);
        }

        // リロード処理開始
        StartCoroutine(ReloadAmmoRoutine());
        tankStatus.ApplyStun(delay);

        // 発射後硬直 (ShotDelay)
        yield return new WaitForSeconds(delay);

        // ApplyStunで自動的にfalseになるが、念のため
        tankStatus.IsInStun = false;
    }

    [Tooltip("地雷（またはスポーンボックスなどのアイテム）を設置する一連の処理")]
    private IEnumerator PlaceMineRoutine()
    {
        // 1. 硬直開始（設置時の隙を作る）
        float delay = tankStatus.GetData().minePlacementDelay;
        tankStatus.ApplyStun(delay);

        // ★修正: インスペクターで設定した変数（mineSpawnOffsetZ）を使って位置をずらす
        // -1.5なら後ろ、0なら中心、1.5なら前に設置されます。
        Vector3 spawnPos = transform.position + transform.forward * mineSpawnOffsetZ;

        // Prefabを取得して生成
        GameObject mineObj = Instantiate(tankStatus.GetMinePrefab(), spawnPos, Quaternion.identity);

        // ★修正: 取得したオブジェクトに付いているコンポーネントに応じて初期化を振り分ける（関係性の分離）
        if (mineObj.TryGetComponent(out MineController mine))
        {
            // 通常の地雷だった場合
            mine.Init(tankStatus, tankStatus.GetMineData());
            tankStatus.OnMinePlaced();
        }
        else if (mineObj.TryGetComponent(out RobotBombController robot))
        {
            // ロボットボムだった場合
            robot.Init(tankStatus, tankStatus.GetMineData());
            tankStatus.OnMinePlaced();
        }
        else if (mineObj.TryGetComponent(out TankSpawnerBox spawner))
        {
            // タンクスポーンボックスだった場合
            spawner.Init(tankStatus, tankStatus.team);
            tankStatus.ActiveMineCount++; // スポーンボックス用のカウント増加
        }

        // 2. 設置硬直時間待機
        yield return new WaitForSeconds(delay);

        // 3. 硬直解除
        tankStatus.IsInStun = false;

        // 4. クールダウン待機（ボタン連打防止）
        yield return new WaitForSeconds(tankStatus.GetData().mineCooldown);
    }

    // ★修正: リロード時に「合計最大弾数」まで回復させる
    private IEnumerator ReloadAmmoRoutine()
    {
        // クールタイム待機
        yield return new WaitForSeconds(tankStatus.GetData().ammoCooldown);

        // ★重要: ここで必ず「最新の合計最大弾数」を取得する
        // TankStatus.GetTotalMaxAmmo() は (基本値 + ボーナス) を返すはず
        int totalMax = tankStatus.GetTotalMaxAmmo();

        // 現在の弾数が最大値未満なら回復
        if (_currentAmmoCount < totalMax)
        {
            _currentAmmoCount++;
        }
    }
    public void OnMaxAmmoIncreased()
    {
        // 撃てる弾を1発増やす
        // （※ "currentAmmo" の部分は、実際のPlayerTankController内での「現在の弾数」の変数名に合わせてください。例: _currentAmmo など）
        _currentAmmoCount++;
    }
}