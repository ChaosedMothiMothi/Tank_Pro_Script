using UnityEngine;
using System.Collections.Generic;

public class ItemController : MonoBehaviour
{
    [SerializeField] private ItemType itemType;

    [Header("Equipment Settings")]
    [Tooltip("Shield: ShieldData / Change系: Prefab / DevilMineLeaker: 地雷 / Devil666: 使用する弾のPrefab")]
    [SerializeField] private ShieldData shieldDataToGive;
    [SerializeField] private GameObject equipmentPrefabToGive;

    [SerializeField] private float rotationSpeed = 100f;

    private readonly HashSet<TankStatus> _passThroughTanks = new HashSet<TankStatus>();

    private void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }

    private static bool IsBuffPickupItem(ItemType type)
    {
        return type != ItemType.Shield
            && type != ItemType.ChangeShell
            && type != ItemType.ChangeMine
            && type != ItemType.ExtraLife;
    }

    private void OnTriggerEnter(Collider other)
    {
        TankStatus status = other.GetComponentInParent<TankStatus>();
        if (status == null) status = other.GetComponent<TankStatus>();

        if (status != null)
        {
            if (IsBuffPickupItem(itemType) && !status.canReceiveBuffs)
            {
                PassThroughTank(status, other);
                return;
            }

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
                case ItemType.ExtraLife:
                    // ★追加: 落ちている1UPを取った時
                    if (GameManager.Instance != null) GameManager.Instance.AddPlayerLife();
                    break;
                case ItemType.DevilMineLeaker:
                    status.ApplyPowerUp(ItemType.DevilMineLeaker, equipmentPrefabToGive);
                    break;
                case ItemType.Devil666:
                    status.ApplyPowerUp(ItemType.Devil666, equipmentPrefabToGive);
                    break;
                default:
                    // ステータスアップ系はすべて種類を渡すだけで、内部のレベルが上がる
                    status.ApplyPowerUp(itemType);
                    break;
            }

            Destroy(gameObject);
        }
    }

    private void PassThroughTank(TankStatus status, Collider other)
    {
        if (status == null || _passThroughTanks.Contains(status)) return;
        _passThroughTanks.Add(status);

        Collider[] itemCols = GetComponentsInChildren<Collider>();
        Transform tankRoot = status.transform;
        Collider[] tankCols = tankRoot.GetComponentsInChildren<Collider>();

        foreach (var itemCol in itemCols)
        {
            if (itemCol == null) continue;
            foreach (var tankCol in tankCols)
            {
                if (tankCol != null)
                    Physics.IgnoreCollision(itemCol, tankCol, true);
            }
        }
    }

}