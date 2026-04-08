using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[Tooltip("アイテムボックスの管理。破壊されるとパーツかアイテムを重みに応じてドロップする。")]
public class ItemBoxController : MonoBehaviour
{
    [System.Serializable]
    public class ItemDropData
    {
        public GameObject itemPrefab;
        public int weight = 10;
    }

    [Header("Status")]
    public int maxHp = 1;
    private int _currentHp;
    private bool _isDead = false;

    [Header("Drop Settings")]
    public int partsWeight = 50;
    public int itemWeight = 50;
    public int partsDropCount = 1;

    // ★アイテムボックスの配置番号（これで撃破を管理）
    public int spawnIndex = -1;

    public GameObject partsPrefabOverride;
    public List<ItemDropData> itemDropList;

    [Header("Destruction")]
    public GameObject explosionEffectPrefab;

    private void Start()
    {
        _currentHp = maxHp;
        if (maxHp >= 10 && HPBarManager.Instance != null)
        {
            HPBarManager.Instance.RegisterItemBox(this);
        }
    }

    // ★SpawnManagerから番号を受け取る関数
    public void SetSpawnIndex(int index)
    {
        spawnIndex = index;
    }

    public void TakeDamage(int damage, TankStatus attacker = null)
    {
        if (_isDead) return;

        _currentHp -= damage;

        if (HPBarManager.Instance != null)
        {
            HPBarManager.Instance.UpdateItemBoxHP(this, _currentHp, maxHp);
        }

        if (_currentHp <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        _isDead = true;

        // ★ここで「この番号の箱が壊れた」とGlobalGameManagerに伝える
        if (GlobalGameManager.Instance != null && spawnIndex >= 0)
        {
            GlobalGameManager.Instance.RecordDefeat(spawnIndex);
            Debug.Log($"<color=cyan>[ItemBox]</color> 箱(ID:{spawnIndex})の破壊を記録しました。");
        }

        DropLoot();
        StartCoroutine(DestructionRoutine());
    }

    private void DropLoot()
    {
        int totalCategoryWeight = partsWeight + itemWeight;
        int categoryRoll = Random.Range(0, totalCategoryWeight);

        if (categoryRoll < partsWeight)
        {
            GameObject partsPrefab = partsPrefabOverride;
            if (partsPrefab == null && GameManager.Instance != null) partsPrefab = GameManager.Instance.GetPartsItemPrefab();

            if (partsPrefab != null)
            {
                for (int i = 0; i < partsDropCount; i++)
                {
                    GameObject obj = Instantiate(partsPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
                    ApplyDropForce(obj);
                }
            }
        }
        else
        {
            if (itemDropList != null && itemDropList.Count > 0)
            {
                int totalItemWeight = 0;
                foreach (var item in itemDropList) totalItemWeight += item.weight;

                int itemRoll = Random.Range(0, totalItemWeight);
                int currentWeight = 0;
                GameObject selectedItem = null;

                foreach (var item in itemDropList)
                {
                    currentWeight += item.weight;
                    if (itemRoll < currentWeight)
                    {
                        selectedItem = item.itemPrefab;
                        break;
                    }
                }

                if (selectedItem != null)
                {
                    GameObject obj = Instantiate(selectedItem, transform.position + Vector3.up * 0.5f, Quaternion.identity);
                    ApplyDropForce(obj);
                }
            }
        }
    }

    private void ApplyDropForce(GameObject obj)
    {
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();

        Vector3 force = Vector3.up * 6.0f + Random.insideUnitSphere * 3.0f;
        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * 150f, ForceMode.Impulse);
    }

    private IEnumerator DestructionRoutine()
    {
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        if (explosionEffectPrefab != null)
        {
            GameObject effect = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 2.0f);
        }
        else if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayExplosion(transform.position);
        }

        yield return new WaitForSeconds(0.1f);
        Destroy(gameObject);
    }
}