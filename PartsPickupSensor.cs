using UnityEngine;

[Tooltip("プレイヤーがパーツに触れたかを検知するセンサー（IsTrigger専用）")]
public class PartsPickupSensor : MonoBehaviour
{
    private PartsItemController _parentController;

    private void Awake()
    {
        // 親オブジェクトについている本体スクリプトを取得
        _parentController = GetComponentInParent<PartsItemController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_parentController == null) return;

        TankStatus tank = other.GetComponentInParent<TankStatus>();
        if (tank != null)
        {
            // 親の取得処理を呼び出す
            _parentController.OnCollected(tank);
        }
    }
}