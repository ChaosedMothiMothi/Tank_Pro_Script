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
        [Tooltip("このアイテムが選ばれる重み（確率）")]
        public int weight = 10;
    }

    [Header("Status")]
    public int maxHp = 1;
    private int _currentHp;
    private bool _isDead = false;

    [Header("Drop Settings")]
    [Tooltip("パーツが落ちる重み")]
    public int partsWeight = 50;
    [Tooltip("アイテムが落ちる重み")]
    public int itemWeight = 50;

    [Tooltip("パーツが選ばれた時に落とす数")]
    public int partsDropCount = 1;

    public int spawnIndex = -1;

    // ★追加: GameMangerに頼らず、直接プレハブを指定できるようにしました
    [Tooltip("ドロップするパーツのプレハブ（未指定の場合はGameManagerから取得）")]
    public GameObject partsPrefabOverride;

    [Tooltip("アイテムが選ばれた時の、各アイテムのドロップ重みリスト")]
    public List<ItemDropData> itemDropList;

    [Header("Destruction")]
    [Tooltip("箱が壊れた時に再生する爆発エフェクト")]
    public GameObject explosionEffectPrefab;

    private void Start()
    {
        _currentHp = maxHp;
        // ★追加: HP10以上なら登録
        if (maxHp >= 10 && HPBarManager.Instance != null)
        {
            HPBarManager.Instance.RegisterItemBox(this);
        }
    }

    // 弾や爆風から呼ばれるダメージ処理
    public void TakeDamage(int damage, TankStatus attacker = null)
    {
        if (_isDead) return;

        // ① まず確実に現在のHPを減らす
        _currentHp -= damage;

        // ② 減った「後」のHPを、HPバー（UI）に伝える
        if (HPBarManager.Instance != null)
        {
            HPBarManager.Instance.UpdateItemBoxHP(this, _currentHp, maxHp);
        }

        // ③ HPが0以下になったら破壊（Die）処理へ
        if (_currentHp <= 0)
        {
            Die();
        }
    }

    public void SetSpawnIndex(int index) { spawnIndex = index; }

    private void Die()
    {
        _isDead = true;
        DropLoot();
        StartCoroutine(DestructionRoutine());
        if (GlobalGameManager.Instance != null && spawnIndex >= 0)
        {
            GlobalGameManager.Instance.RecordDefeat(spawnIndex);
        }
    }

    private void DropLoot()
    {
        int totalCategoryWeight = partsWeight + itemWeight;
        int categoryRoll = Random.Range(0, totalCategoryWeight);

        if (categoryRoll < partsWeight)
        {
            // --- パーツのドロップ ---
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
            // --- アイテムのドロップ ---
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

    // ★修正: 中身のパーツやアイテムをもっと勢いよく飛び散らせ��
    private void ApplyDropForce(GameObject obj)
    {
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();

        // 箱から「ポンッ！」と高く弾け飛ぶように力を強くする
        Vector3 force = Vector3.up * 6.0f + Random.insideUnitSphere * 3.0f;
        rb.AddForce(force, ForceMode.Impulse);

        // クルクルと激しく回転しながら飛ぶ
        rb.AddTorque(Random.insideUnitSphere * 150f, ForceMode.Impulse);
    }

    // ★修正: バラバラにする物理演算をやめ、爆発エフェクトを出してそのまま消す
    private IEnumerator DestructionRoutine()
    {
        // 本体のコライダーと見た目をオフにする（即座に消えたように見せる）
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        // 爆発エフェクトの再生
        if (explosionEffectPrefab != null)
        {
            GameObject effect = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 2.0f); // 2秒後にエフェクトを消去
        }
        else if (EffectManager.Instance != null)
        {
            // プレハブが指定されていなければManagerの共通爆発を使う
            EffectManager.Instance.PlayExplosion(transform.position);
        }

        // コルーチンを少しだけ待たせてから本体を完全消去
        yield return new WaitForSeconds(0.1f);
        Destroy(gameObject);
    }
}