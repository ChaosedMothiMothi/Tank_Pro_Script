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

    private bool IsGameActive => GameManager.Instance == null || !GameManager.Instance.IsGameFinished();

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsGameActive) { _moveInput = Vector2.zero; return; }
        _moveInput = context.ReadValue<Vector2>();
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

    public void OnMine(InputAction.CallbackContext context)
    {
        if (!IsGameActive) return;

        if (!context.performed || !IsGameActive || tankStatus.IsInStun) return;

        // 設置上限チェック
        if (tankStatus.ActiveMineCount >= tankStatus.GetTotalMineLimit()) return;

        StartCoroutine(PlaceSpawnerBoxRoutine());
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

    // ★修正: 地雷設置時の硬直（Stun）を復元
    private IEnumerator PlaceMineRoutine()
    {
        // 1. 硬直開始
        // ★修正: 単にフラグを立てるのではなく、ApplyStunでタイマーを設定する
        float delay = tankStatus.GetData().minePlacementDelay;
        tankStatus.ApplyStun(delay);

        GameObject mineObj = Instantiate(tankStatus.GetMinePrefab(), transform.position, Quaternion.identity);
        MineController mine = mineObj.GetComponent<MineController>();
        if (mine != null) { mine.Init(tankStatus, tankStatus.GetMineData()); tankStatus.OnMinePlaced(); }
        else
        {
            RobotBombController robot = mineObj.GetComponent<RobotBombController>();
            if (robot != null) { robot.Init(tankStatus, tankStatus.GetMineData()); tankStatus.OnMinePlaced(); }
        }

        // 2. 設置硬直時間待機 (minePlacementDelay)
        yield return new WaitForSeconds(delay);

        // 3. 硬直��除 (ApplyStunのタイマー切れで自動解除されるが、シーケンスとして明示)
        tankStatus.IsInStun = false;

        // 4. クールダウン待機 (連打防止)
        yield return new WaitForSeconds(tankStatus.GetData().mineCooldown);
    }

    // ★追加: TankStatusのSendMessageから呼ばれるメソッド
    // これがないと、最大数は増えても撃てる回数（_currentAmmoCount）が増えません
    private void OnMaxAmmoIncreased()
    {
        // ボーナス込みの最新の最大弾数を取得
        int totalMax = tankStatus.GetTotalMaxAmmo();

        // ★修正: 単純にインクリメントするだけでなく、最大値を超えないようにキャップする
        // ���イテムを取った瞬間、最大枠が増えた分だけ現在弾数も増やす
        if (_currentAmmoCount < totalMax)
        {
            _currentAmmoCount++;
        }

        Debug.Log($"Max Ammo Up! Now: {_currentAmmoCount} / {totalMax}");
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
    private IEnumerator PlaceSpawnerBoxRoutine()
    {
        // 硬直開始
        float delay = tankStatus.GetData().minePlacementDelay;
        tankStatus.ApplyStun(delay);

        // 設置位置（戦車の後ろ側など）
        Vector3 spawnPos = transform.position - transform.forward * 1.5f;
        GameObject prefab = tankStatus.GetMinePrefab(); // ここにTankSpawnerBoxのPrefabを指定しておく

        if (prefab != null)
        {
            GameObject boxObj = Instantiate(prefab, spawnPos, Quaternion.identity);
            tankStatus.ActiveMineCount++;

            // 箱の初期化
            TankSpawnerBox spawner = boxObj.GetComponent<TankSpawnerBox>();
            if (spawner != null)
            {
                spawner.Init(tankStatus, tankStatus.team);
            }
        }

        yield return new WaitForSeconds(delay);
        tankStatus.IsInStun = false;
    }
}