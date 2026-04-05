using UnityEngine;
using System.Collections;

public class MovingWall : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 2.0f;
    public float waitTime = 1.0f;

    private int _currentIndex = 0;
    private Rigidbody _rb;
    private bool _isWaiting = false;

    private void Start()
    {
        _rb = GetComponent<Rigidbody>();
        // 日本語：壁自身は「物理無効(Kinematic)」にして、スクリプトで位置を制御
        _rb.isKinematic = true;
        if (waypoints.Length > 0) transform.position = waypoints[0].position;
    }

    private void FixedUpdate()
    {
        // ★追加: ゲーム開始前、または終了時には壁の移動を完全に止める
        if (GameManager.Instance != null && (!GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished()))
        {
            return; // 物理移動をせず待機
        }

        if (waypoints.Length < 2 || _isWaiting) return;

        Vector3 targetPos = waypoints[_currentIndex].position;
        // 日本語：MovePosition を使うことで、重なっているオブジェクトを物理的に押し退けます
        Vector3 newPos = Vector3.MoveTowards(_rb.position, targetPos, speed * Time.fixedDeltaTime);
        _rb.MovePosition(newPos);

        if (Vector3.Distance(newPos, targetPos) < 0.05f)
        {
            StartCoroutine(WaitRoutine());
        }
    }

    private IEnumerator WaitRoutine()
    {
        _isWaiting = true;
        yield return new WaitForSeconds(waitTime);
        _currentIndex = (_currentIndex + 1) % waypoints.Length;
        _isWaiting = false;
    }
}