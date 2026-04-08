using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ExchangeBoxController : MonoBehaviour
{
    [Header("Box Settings")]
    public bool isRandom = true;
    public ExchangeItemData fixedItem;
    public List<ExchangeItemData> randomPool = new List<ExchangeItemData>();

    [Header("Rarity Weights")]
    public int weightNormal = 50, weightRare = 30, weightSR = 15, weightUR = 5;

    [Header("Visuals")]
    public Transform visualTransform;
    public Vector3 textOffset = new Vector3(0, 1.5f, 0);
    public float textSizeMultiplier = 0.1f;

    private TextMesh _infoTextInstance;
    private ExchangeItemData _assignedItem;
    private int _requiredCost = 0;
    private bool _isUsed = false;
    private bool _isShaking = false;

    private static HashSet<ItemType> _spawnedTypesInStage = new HashSet<ItemType>();
    private static int _lastClearFrame = -1;

    private void Awake()
    {
        if (_lastClearFrame != Time.frameCount)
        {
            _spawnedTypesInStage.Clear();
            _lastClearFrame = Time.frameCount;
        }
    }

    private IEnumerator Start()
    {
        GameObject textObj = new GameObject("ExchangeInfoText");
        textObj.transform.SetParent(transform);
        textObj.transform.position = transform.position + textOffset;

        _infoTextInstance = textObj.AddComponent<TextMesh>();
        _infoTextInstance.characterSize = textSizeMultiplier;
        _infoTextInstance.fontSize = 60;
        _infoTextInstance.anchor = TextAnchor.MiddleCenter;
        _infoTextInstance.alignment = TextAlignment.Center;
        _infoTextInstance.color = Color.white;

        yield return new WaitForSeconds(0.2f);

        TankStatus player = GetPlayer();

        if (isRandom) AssignRandomItem(player);
        else _assignedItem = fixedItem;

        CalculateCostAndDisplay(player);
    }

    private void Update()
    {
        if (_infoTextInstance != null && Camera.main != null)
        {
            _infoTextInstance.transform.rotation = Camera.main.transform.rotation;
        }
    }

    private TankStatus GetPlayer()
    {
        foreach (var tank in FindObjectsByType<TankStatus>(FindObjectsSortMode.None))
        {
            if (tank.team == TeamType.Blue) return tank;
        }
        return null;
    }

    private void AssignRandomItem(TankStatus player)
    {
        if (randomPool.Count == 0) return;
        List<ExchangeItemData> validItems = new List<ExchangeItemData>();

        foreach (var item in randomPool)
        {
            if (item == null || _spawnedTypesInStage.Contains(item.itemType)) continue;
            if (player != null && IsMaxLevel(player, item.itemType)) continue;
            validItems.Add(item);
        }

        if (validItems.Count == 0) validItems.AddRange(randomPool);

        _assignedItem = RollRarityAndPick(validItems);
        if (_assignedItem != null) _spawnedTypesInStage.Add(_assignedItem.itemType);
    }

    private ExchangeItemData RollRarityAndPick(List<ExchangeItemData> pool)
    {
        int total = weightNormal + weightRare + weightSR + weightUR;
        int roll = Random.Range(0, total);
        RarityType targetRarity = roll < weightUR ? RarityType.UR : roll < weightUR + weightSR ? RarityType.SR : roll < weightUR + weightSR + weightRare ? RarityType.Rare : RarityType.Normal;

        List<ExchangeItemData> rarityMatch = pool.FindAll(i => i.rarity == targetRarity);
        return rarityMatch.Count > 0 ? rarityMatch[Random.Range(0, rarityMatch.Count)] : pool[Random.Range(0, pool.Count)];
    }

    private void CalculateCostAndDisplay(TankStatus player)
    {
        if (_assignedItem == null) return;

        int currentLevel = (player != null) ? GetCurrentLevel(player, _assignedItem.itemType) : 0;

        if (_assignedItem.costsByCurrentLevel != null && _assignedItem.costsByCurrentLevel.Length > 0)
        {
            int costIndex = Mathf.Min(currentLevel, _assignedItem.costsByCurrentLevel.Length - 1);
            _requiredCost = _assignedItem.costsByCurrentLevel[costIndex];
        }
        else
        {
            _requiredCost = 10;
        }

        if (_infoTextInstance != null)
        {
            _infoTextInstance.text = $"{_assignedItem.itemName}\nCost: {_requiredCost}";
            _infoTextInstance.color = GetRarityColor(_assignedItem.rarity);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isUsed) return;

        TankStatus status = other.GetComponentInParent<TankStatus>();
        if (status == null) status = other.GetComponent<TankStatus>();

        if (status != null && status.team == TeamType.Blue) TryBuyItem(status);
    }

    private void TryBuyItem(TankStatus player)
    {
        if (_assignedItem == null) return;

        if (IsMaxLevel(player, _assignedItem.itemType))
        {
            Debug.Log("既にレベルMAXです！");
            if (!_isShaking) StartCoroutine(ShakeRoutine());
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.CurrentParts >= _requiredCost)
        {
            GameManager.Instance.ConsumeParts(_requiredCost);

            _isUsed = true;
            ApplyEffect(player);

            if (EffectManager.Instance != null) EffectManager.Instance.PlayExplosion(transform.position);

            if (_infoTextInstance != null) Destroy(_infoTextInstance.gameObject);
            Destroy(gameObject);
        }
        else
        {
            Debug.Log($"パーツが足りません！ (必要: {_requiredCost} / 所持: {(GameManager.Instance != null ? GameManager.Instance.CurrentParts.ToString() : "0")})");
            if (!_isShaking) StartCoroutine(ShakeRoutine());
        }
    }

    private void ApplyEffect(TankStatus player)
    {
        switch (_assignedItem.itemType)
        {
            case ItemType.Shield: if (_assignedItem.shieldData != null) player.EquipShield(_assignedItem.shieldData); break;
            case ItemType.ChangeShell: if (_assignedItem.equipmentPrefab != null) player.ChangeShellPrefab(_assignedItem.equipmentPrefab); break;
            case ItemType.ChangeMine: if (_assignedItem.equipmentPrefab != null) player.ChangeMinePrefab(_assignedItem.equipmentPrefab); break;
            default: player.ApplyPowerUp(_assignedItem.itemType); break;
        }
    }

    private IEnumerator ShakeRoutine()
    {
        _isShaking = true;
        Transform target = (visualTransform != null) ? visualTransform : transform;
        Vector3 originalPos = target.localPosition;

        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            target.localPosition = originalPos + (Vector3)Random.insideUnitCircle * 0.2f;
            elapsed += Time.deltaTime;
            yield return null;
        }

        target.localPosition = originalPos;
        _isShaking = false;
    }

    private int GetCurrentLevel(TankStatus player, ItemType type)
    {
        switch (type)
        {
            case ItemType.BouncePlus: return player.levelBounces;
            case ItemType.MaxAmmoPlus: return player.levelMaxAmmo;
            case ItemType.MoveSpeedUp: return player.levelMoveSpeed;
            case ItemType.ShellSpeedUp: return player.levelShellSpeed;
            case ItemType.TurnSpeedUp: return player.levelRotationSpeed;
            case ItemType.MineLimitUp: return player.levelMineLimit;
            case ItemType.ShellDamageUp: return player.levelShellDamage;
            case ItemType.MineDamageUp: return player.levelMineDamage;
            default: return 0;
        }
    }

    private bool IsMaxLevel(TankStatus player, ItemType type)
    {
        // ★修正: 残機アップとバーサーカーは「レベルMAX」の概念を無くし、いつでも買えるようにする
        if (type == ItemType.ExtraLife || type == ItemType.BerserkerMode) return false;

        int lvl = GetCurrentLevel(player, type);
        switch (type)
        {
            case ItemType.BouncePlus: return lvl >= 5;
            case ItemType.MaxAmmoPlus: return lvl >= 3;
            case ItemType.ShellDamageUp:
            case ItemType.MineDamageUp: return lvl >= 10;
            case ItemType.Shield:
            case ItemType.ChangeShell:
            case ItemType.ChangeMine: return false;
            default: return lvl >= 5;
        }
    }

    private Color GetRarityColor(RarityType rarity)
    {
        switch (rarity)
        {
            case RarityType.Normal: return Color.white;
            case RarityType.Rare: return Color.cyan;
            case RarityType.SR: return Color.magenta;
            case RarityType.UR: return Color.yellow;
            default: return Color.white;
        }
    }
}