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
    [SerializeField] private float mineSpawnOffsetZ = 2f;

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
        _currentAmmoCount = tankStatus.GetTotalMaxAmmo();
    }

    private bool IsGameActive => GameManager.Instance == null || (GameManager.Instance.IsGameStarted && !GameManager.Instance.IsGameFinished());

    public void OnMove(InputAction.CallbackContext context)
    {
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
        if (!context.performed || tankStatus.IsInStun) return;
        if (tankStatus.ActiveMineCount >= tankStatus.GetTotalMineLimit()) return;
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

        if (tankStatus.IsInStun)
        {
            _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
            return;
        }

        // ★バーサーカーモード中の移動
        if (tankStatus.isBerserkerMode)
        {
            HandleBerserkerMovement();
        }
        else
        {
            // 通常の移動
            if (_moveInput.sqrMagnitude > 0.01f) HandleMovement();
            else _rb.linearVelocity = Vector3.Lerp(_rb.linearVelocity, new Vector3(0, _rb.linearVelocity.y, 0), 0.2f);
        }
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
        float currentAngle = _rb.rotation.eulerAngles.y;
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

    // ★修正: バーサーカーモードの特別な移動処理
    private void HandleBerserkerMovement()
    {
        float currentAngle = _rb.rotation.eulerAngles.y;
        float rotationSpeed = tankStatus.GetCurrentRotationSpeed();

        // プレイヤーが移動キーを入力している場合のみ、その方向へ旋回する
        if (_moveInput.sqrMagnitude > 0.01f)
        {
            // 後退処理を無視し、入力された方向に素直に振り向くための絶対角度を計算
            float targetDirAngle = Mathf.Atan2(_moveInput.x, _moveInput.y) * Mathf.Rad2Deg;

            float nextAngle = Mathf.MoveTowardsAngle(currentAngle, targetDirAngle, rotationSpeed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.Euler(0, nextAngle, 0));

            // 目標の角度に到達していない（旋回中）の場合は、前進せずその場で回る
            if (Mathf.Abs(Mathf.DeltaAngle(nextAngle, targetDirAngle)) >= 2.0f)
            {
                _rb.linearVelocity = new Vector3(0, _rb.linearVelocity.y, 0);
                return; // 旋回中は前進処理をスキップ
            }
        }

        // 入力がない、または旋回が完了している場合は常に正面へ前進し続ける
        float currentSpeed = tankStatus.GetCurrentMoveSpeed(); // TankStatus側で既に2倍になっています
        Vector3 vel = transform.forward * currentSpeed;
        _rb.linearVelocity = new Vector3(vel.x, _rb.linearVelocity.y, vel.z);
    }

    private IEnumerator ShootRoutine()
    {
        _currentAmmoCount--;

        float delay = tankStatus.GetData().shotDelay;
        tankStatus.ApplyStun(delay);

        if (EffectManager.Instance != null && firePoint != null) EffectManager.Instance.PlayMuzzleFlash(firePoint);
        if (tankStatus.GetShellPrefab() != null)
        {
            EffectManager.Instance.ShotSound();
            GameObject shellObj = Instantiate(tankStatus.GetShellPrefab(), firePoint.position, firePoint.rotation);
            shellObj.GetComponent<ShellController>()?.Launch(this.gameObject, 0);
        }

        StartCoroutine(ReloadAmmoRoutine());
        tankStatus.ApplyStun(delay);

        yield return new WaitForSeconds(delay);
        tankStatus.IsInStun = false;
    }

    private IEnumerator PlaceMineRoutine()
    {
        float delay = tankStatus.GetData().minePlacementDelay;
        tankStatus.ApplyStun(delay);

        Vector3 spawnPos = transform.position + transform.forward * mineSpawnOffsetZ;
        GameObject mineObj = Instantiate(tankStatus.GetMinePrefab(), spawnPos, Quaternion.identity);

        if (mineObj.TryGetComponent(out MineController mine))
        {
            mine.Init(tankStatus, tankStatus.GetMineData());
            tankStatus.OnMinePlaced();
        }
        else if (mineObj.TryGetComponent(out RobotBombController robot))
        {
            robot.Init(tankStatus, tankStatus.GetMineData());
            tankStatus.OnMinePlaced();
        }
        else if (mineObj.TryGetComponent(out TankSpawnerBox spawner))
        {
            spawner.Init(tankStatus, tankStatus.team);
            tankStatus.ActiveMineCount++;
        }

        yield return new WaitForSeconds(delay);
        tankStatus.IsInStun = false;

        yield return new WaitForSeconds(tankStatus.GetData().mineCooldown);
    }

    private IEnumerator ReloadAmmoRoutine()
    {
        yield return new WaitForSeconds(tankStatus.GetData().ammoCooldown);
        int totalMax = tankStatus.GetTotalMaxAmmo();
        if (_currentAmmoCount < totalMax) _currentAmmoCount++;
    }

    public void OnMaxAmmoIncreased()
    {
        _currentAmmoCount++;
    }
}