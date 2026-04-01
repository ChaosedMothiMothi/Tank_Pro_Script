using UnityEngine;

public class ItemController : MonoBehaviour
{
    [SerializeField] private ItemType itemType;

    [Header("Equipment Settings")]
    [Tooltip("Shieldの場合: ShieldData / Change系の場合: 新しいPrefab")]
    [SerializeField] private ShieldData shieldDataToGive;
    [SerializeField] private GameObject equipmentPrefabToGive;

    [Header("Status Boost Amount (ステータス強化系)")]
    [Tooltip("移動速度アップの増加量 (例: 1.5)")]
    [SerializeField] private float moveSpeedAmount = 1.5f;

    [Tooltip("弾速アップの増加量 (例: 5.0)")]
    [SerializeField] private float shellSpeedAmount = 5.0f;

    [Tooltip("旋回速度アップの増加量 (例: 20.0)")]
    [SerializeField] private float turnSpeedAmount = 20.0f;

    [SerializeField] private float rotationSpeed = 100f;

    private void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        TankStatus status = other.GetComponentInParent<TankStatus>();
        if (status == null) status = other.GetComponent<TankStatus>();

        if (status != null)
        {
            switch (itemType)
            {
                case ItemType.Shield:
                    if (shieldDataToGive != null) status.EquipShield(shieldDataToGive);
                    break;

                case ItemType.ChangeShell:
                    if (equipmentPrefabToGive != null) status.ChangeShellPrefab(equipmentPrefabToGive);
                    break;

                case ItemType.ChangeMine:
                    if (equipmentPrefabToGive != null) status.ChangeMinePrefab(equipmentPrefabToGive);
                    break;

                // ★変更: ステータスアップ系は数値を渡す
                case ItemType.MoveSpeedUp:
                    status.ApplyPowerUp(itemType, moveSpeedAmount);
                    break;

                case ItemType.ShellSpeedUp:
                    status.ApplyPowerUp(itemType, shellSpeedAmount);
                    break;

                case ItemType.TurnSpeedUp: // ★追加
                    status.ApplyPowerUp(itemType, turnSpeedAmount);
                    break;

                default:
                    // +1系（MaxAmmoPlus, BouncePlusなど）は数値を渡さなくてOK
                    status.ApplyPowerUp(itemType);
                    break;
            }

            Debug.Log($"<color=cyan>アイテム獲得!</color> {status.name} が {itemType} を取得");
            Destroy(gameObject);
        }
    }
}