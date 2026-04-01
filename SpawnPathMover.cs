using UnityEngine;
using System.Collections;

/// <summary>
/// スポーン直後の強制移動を担当するコンポーネント。
/// AIや射撃をブロックし、物理移動のみを行います。
/// </summary>
public class SpawnPathMover : MonoBehaviour
{
    private Transform[] _pathPoints;
    private float _speed;
    private System.Action _onComplete;
    private int _currentIndex = 0;
    private Rigidbody _rb;

    public void Initialize(Transform[] points, float speed, System.Action onComplete)
    {
        _pathPoints = points;
        _speed = speed;
        _onComplete = onComplete;
        _currentIndex = 0;
        _rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (_pathPoints == null || _currentIndex >= _pathPoints.Length)
        {
            Complete();
            return;
        }

        Transform targetPoint = _pathPoints[_currentIndex];
        Vector3 targetPos = targetPoint.position;
        // 高さは自分の高さを維持（坂道などを考慮する場合はRaycast等が必要だが、簡易的にyは無視）
        Vector3 myPos = transform.position;
        Vector3 dir = (new Vector3(targetPos.x, myPos.y, targetPos.z) - myPos).normalized;
        float dist = Vector3.Distance(new Vector3(targetPos.x, 0, targetPos.z), new Vector3(myPos.x, 0, myPos.z));

        // 回転
        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            _rb.MoveRotation(Quaternion.RotateTowards(transform.rotation, targetRot, 180f * Time.fixedDeltaTime));
        }

        // 移動
        Vector3 velocity = dir * _speed;
        _rb.linearVelocity = new Vector3(velocity.x, _rb.linearVelocity.y, velocity.z);

        // 到着判定 (半径1m以内)
        if (dist < 1.0f)
        {
            _currentIndex++;
        }
    }

    private void Complete()
    {
        // 速度を殺す
        if (_rb != null) _rb.linearVelocity = Vector3.zero;

        // コールバック実行（AI有効化など）
        _onComplete?.Invoke();

        // お役御免
        Destroy(this);
    }
}