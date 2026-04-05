using UnityEngine;
using System.Collections;

[Tooltip("ドロップしたパーツの本体（物理挙動とマグネット取得処理を管理）")]
public class PartsItemController : MonoBehaviour
{
    private bool _isCollected = false;
    private TankStatus _magnetTarget = null;
    private bool _isMagnetActive = false;
    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        // マグネットモード起動中ならターゲットへ向かって飛ぶ
        if (_isMagnetActive && _magnetTarget != null && !_magnetTarget.IsDead)
        {
            // プレイヤーに向けて高速で吸い込まれる
            transform.position = Vector3.MoveTowards(transform.position, _magnetTarget.transform.position + Vector3.up, Time.deltaTime * 30f);

            // 距離が近くなったら強制的に獲得
            if (Vector3.Distance(transform.position, _magnetTarget.transform.position + Vector3.up) < 1.0f)
            {
                OnCollected(_magnetTarget);
            }
        }
    }

    // ★追加: 吸い込みモード開始（EnemyTankControllerから呼ばれる）
    public void StartMagneticEffect(TankStatus target)
    {
        _magnetTarget = target;
        StartCoroutine(MagneticRoutine());
    }

    private IEnumerator MagneticRoutine()
    {
        // 最初は通常通り物理でポロっと落ちるので、少しだけ待つ（テンポ重視で0.8秒）
        yield return new WaitForSeconds(0.8f);

        _isMagnetActive = true;

        // 物理演算を切り、壁などをすり抜けるようにする
        if (_rb != null) _rb.isKinematic = true;

        Collider[] cols = GetComponentsInChildren<Collider>();
        foreach (var c in cols) c.enabled = false;
    }

    // センサー（またはマグネット）から呼ばれる関数
    public void OnCollected(TankStatus tank)
    {
        if (_isCollected) return;

        // プレイヤーチームか、もしくはマグネットターゲット自身なら獲得
        if (tank != null && (tank.team == TeamType.Blue || tank == _magnetTarget) && !tank.IsDead)
        {
            // ★横取り防止: マグネットモード中は、ラストアタックしたターゲット以外は絶対に拾えない
            if (_magnetTarget != null && tank != _magnetTarget) return;

            _isCollected = true;

            if (GameManager.Instance != null)
            {
                GameManager.Instance.AddParts(1);
            }

            Destroy(gameObject);
        }
    }
}