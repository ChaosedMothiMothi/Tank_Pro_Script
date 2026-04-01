using UnityEngine;

public class MineSensor : MonoBehaviour
{
    private MineController _parent;

    private void Awake()
    {
        _parent = GetComponentInParent<MineController>();
    }

    // 日本語：センサーが何かに触れたら、親の MineController に通知する
    private void OnCollisionEnter(Collision collision)
    {
        if (_parent != null) _parent.NotifySensorImpact(collision.gameObject);
    }

    // 日本語：爆発判定（IsTrigger）などの検知用
    private void OnTriggerEnter(Collider other)
    {
        if (_parent != null) _parent.NotifySensorImpact(other.gameObject);
    }
}