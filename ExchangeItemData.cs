using UnityEngine;

public enum RarityType { Normal, Rare, SR, UR }

[CreateAssetMenu(fileName = "NewExchangeItem", menuName = "Game/Exchange Item Data")]
public class ExchangeItemData : ScriptableObject
{
    [Header("基本設定")]
    public string itemName = "強化アイテム";
    public ItemType itemType;
    public RarityType rarity = RarityType.Normal;

    [Header("コスト設定")]
    [Tooltip("現在のレベルに応じた要求パーツ数。左からLv0の時、Lv1の時...のコスト。\n装備変更（Change系）は一番左の数値のみ使われます。")]
    public int[] costsByCurrentLevel = { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };

    [Header("装備変更用（Change/Shield系の場合のみセット）")]
    public GameObject equipmentPrefab;
    public ShieldData shieldData;
}