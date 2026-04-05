using UnityEngine;

public class ItemController : MonoBehaviour
{
    [SerializeField] private ItemType itemType;

    [Header("Equipment Settings")]
    [Tooltip("Shieldの場合: ShieldData / Change系の場合: 新しいPrefab")]
    [SerializeField] private ShieldData shieldDataToGive;
    [SerializeField] private GameObject equipmentPrefabToGive;

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
                default:
                    // ステータスアップ系はすべて種類を渡すだけで、内部のレベルが上がる
                    status.ApplyPowerUp(itemType);
                    break;
            }

            Destroy(gameObject);
        }
    }
}