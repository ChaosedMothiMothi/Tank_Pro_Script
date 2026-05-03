using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

[Tooltip("地雷の代わりに設置し、一定時間後に戦車やアイテムを展開するボックス")]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(TankStatus))]
public class TankSpawnerBox : MonoBehaviour
{

    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("出現させるプレハブ")]
        public GameObject prefab;
        [Tooltip("出現する確率の重み（大きいほど出やすい）")]
        public int weight = 10;
    }

    [Header("Box Settings")]
    [Tooltip("ボックス自体の耐久値。展開前に削り切られると壊れる")]
    public int maxHp = 30;

    [Tooltip("設置してから中身が展開（アクティブ）になるまでの時間（秒）")]
    public float timeToSpawn = 3.0f;

    [Tooltip("スポーン時の高さの微調整（地面に埋まらないように上げる）")]
    public float spawnOffsetY = 0.5f;

    [Header("Effects")]
    [Tooltip("展開時（箱が吹き飛ぶ瞬間）に再生するエフェクトのプレハブ")]
    public GameObject spawnEffectPrefab;

    [Header("Prefabs")]
    // ★修正: 従来の単一プレハブ変数を削除し、リストに変更
    [Tooltip("展開させる中身の候補リスト（重み付きでランダムに選ばれます）")]
    public List<SpawnEntry> entitiesToSpawn;

    // ★追加: 実際に選ばれたプレハブを記憶しておく変数
    private GameObject _selectedPrefab;
    private TankStatus _owner;
    private TeamType _team;
    private bool _isProcessed = false;
    private Collider _myCollider;

    // クラスの最初の方にある変数宣言に追加
    private TankStatus _myTankStatus;
    private GameObject _dummyVisual;
    private int _currentBoxHp; // ★追加: 箱専用の独立したHP変数

    public System.Action<TankStatus> OnTankSpawned;

    private void Awake()
    {
        _myCollider = GetComponent<Collider>();
        _myTankStatus = GetComponent<TankStatus>();

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        Vector3 pos = transform.position;
        pos.y = Mathf.Max(pos.y, 0.1f);
        transform.position = pos;
    }

    public void Init(TankStatus owner, TeamType team)
    {
        _owner = owner;
        _team = team;

        _currentBoxHp = maxHp;

        if (_myTankStatus != null)
        {
            _myTankStatus.SetTeam(_team, false, false, -1);
        }

        Collider[] myColliders = GetComponentsInChildren<Collider>();

        if (_owner != null)
        {
            Collider[] ownerColliders = _owner.GetComponentsInChildren<Collider>();

            if (myColliders != null && myColliders.Length > 0 && ownerColliders != null)
            {
                foreach (var myCol in myColliders)
                {
                    foreach (var ownerCol in ownerColliders)
                    {
                        Physics.IgnoreCollision(myCol, ownerCol, true);
                    }
                }
            }
            StartCoroutine(RestoreCollisionRoutine(myColliders, ownerColliders));
        }

        // ★追加: ボックスが設置された瞬間に、中身をランダムに決定する
        _selectedPrefab = GetRandomPrefab();

        CreateDummyVisual();
        StartCoroutine(SpawnRoutine());
    }


    private void Update()
    {
        // ★修正: 箱専用のHPが0以下になったら破壊する
        if (_currentBoxHp <= 0 && !_isProcessed)
        {
            BreakBox();
        }
    }

    // ★追加: 重みを考慮してリストからランダムにプレハブを選ぶメソッド
    private GameObject GetRandomPrefab()
    {
        if (entitiesToSpawn == null || entitiesToSpawn.Count == 0) return null;

        int totalWeight = 0;
        foreach (var entry in entitiesToSpawn) totalWeight += entry.weight;

        int randomValue = Random.Range(0, totalWeight);
        int currentWeight = 0;

        foreach (var entry in entitiesToSpawn)
        {
            currentWeight += entry.weight;
            if (randomValue < currentWeight) return entry.prefab;
        }

        return entitiesToSpawn[0].prefab; // 安全対策
    }

    // ★追加: 箱が破壊された（展開失敗した）時の処理
    private void BreakBox()
    {
        _isProcessed = true;

        if (_myCollider != null) _myCollider.enabled = false;
        if (_dummyVisual != null) Destroy(_dummyVisual);

        if (EffectManager.Instance != null) EffectManager.Instance.PlayExplosion(transform.position);

        // 破壊された時も面パーツを吹き飛ばす
        ScatterChildParts();

        if (_owner != null) _owner.OnMineRemoved();

        // 即座に削除して、通常の戦車の死亡演出をキャンセルする
        Destroy(gameObject);
    }

    private void CreateDummyVisual()
    {
        // ★修正: ランダムで選ばれたプレハブ (_selectedPrefab) を使う
        if (_selectedPrefab == null) return;

        Vector3 spawnPos = transform.position + Vector3.up * spawnOffsetY;
        _dummyVisual = Instantiate(_selectedPrefab, spawnPos, transform.rotation, transform);
        _dummyVisual.transform.localScale = Vector3.one * 0.4f;

        foreach (var mb in _dummyVisual.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
        foreach (var agent in _dummyVisual.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>()) agent.enabled = false;
        foreach (var col in _dummyVisual.GetComponentsInChildren<Collider>()) col.enabled = false;
        foreach (var rb in _dummyVisual.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }
    }

    private IEnumerator RestoreCollisionRoutine(Collider[] myColliders, Collider[] ownerColliders)
    {
        yield return new WaitForSeconds(3.0f);

        if (myColliders != null && ownerColliders != null)
        {
            foreach (var myCol in myColliders)
            {
                if (myCol == null) continue;
                foreach (var ownerCol in ownerColliders)
                {
                    if (ownerCol != null) Physics.IgnoreCollision(myCol, ownerCol, false);
                }
            }
        }
    }

    // ==========================================
    // ★修正: ボックス展開処理（生まれた戦車をボスに通知する）
    // ==========================================
    private IEnumerator SpawnRoutine()
    {
        float timer = 0f;
        Vector3 originalPos = transform.position;

        while (timer < timeToSpawn)
        {
            timer += Time.deltaTime;
            if (timeToSpawn - timer < 1.0f)
            {
                transform.position = originalPos + (Vector3)Random.insideUnitCircle * 0.05f;
            }
            yield return null;
        }

        transform.position = originalPos;

        if (_isProcessed) yield break;
        _isProcessed = true;

        if (_myCollider != null) _myCollider.enabled = false;

        if (spawnEffectPrefab != null)
        {
            GameObject effect = Instantiate(spawnEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 2.0f);
        }
        else if (EffectManager.Instance != null)
        {
            EffectManager.Instance.PlayExplosion(transform.position);
        }

        if (_dummyVisual != null) Destroy(_dummyVisual);

        ScatterChildParts();

        // ★修正: _selectedPrefab から実体を生成する
        if (_selectedPrefab != null)
        {
            Vector3 spawnPos = transform.position + Vector3.up * spawnOffsetY;
            GameObject spawnedObj = Instantiate(_selectedPrefab, spawnPos, transform.rotation);

            TankStatus spawnedTank = spawnedObj.GetComponentInChildren<TankStatus>();
            if (spawnedTank != null)
            {
                spawnedTank.SetTeam(_team, false, false, -1);
                EnemyTankController enemyCtrl = spawnedObj.GetComponentInChildren<EnemyTankController>();
                if (enemyCtrl != null) enemyCtrl.SetDropPartsCount(0);

                spawnedTank.ApplyStun(0.5f);

                // 生まれた戦車をボスに通知
                OnTankSpawned?.Invoke(spawnedTank);
            }
            else
            {
                MineController mine = spawnedObj.GetComponentInChildren<MineController>();
                if (mine != null && _owner != null) mine.Init(_owner, _owner.GetMineData());
                else
                {
                    RobotBombController robot = spawnedObj.GetComponentInChildren<RobotBombController>();
                    if (robot != null && _owner != null) robot.Init(_owner, _owner.GetMineData());
                }
            }

            yield return StartCoroutine(PopOutScaleRoutine(spawnedObj.transform));
        }

        if (_owner != null) _owner.OnMineRemoved();

        Destroy(gameObject);
    }

    private void ScatterChildParts()
    {
        List<Transform> childrenToScatter = new List<Transform>();

        foreach (Transform child in transform)
        {
            if (child.gameObject == _dummyVisual) continue;
            if (child.GetComponent<Renderer>() != null) childrenToScatter.Add(child);
        }

        foreach (Transform part in childrenToScatter)
        {
            part.SetParent(null);

            Rigidbody rb = part.GetComponent<Rigidbody>();
            if (rb == null) rb = part.gameObject.AddComponent<Rigidbody>();

            rb.isKinematic = false;
            rb.useGravity = true;
            rb.mass = 0.5f;

            Vector3 dirFromCenter = (part.position - transform.position).normalized;
            Vector3 force = dirFromCenter * 8.0f + Vector3.up * 5.0f;
            rb.AddForce(force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 500f, ForceMode.Impulse);

            StartCoroutine(ShrinkAndDestroyPart(part.gameObject));
        }
    }

    private IEnumerator ShrinkAndDestroyPart(GameObject part)
    {
        if (part == null) yield break;

        Collider col = part.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Vector3 originalScale = part.transform.localScale;
        float duration = 0.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (part == null) yield break;

            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            part.transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, progress);

            yield return null;
        }

        if (part != null) Destroy(part);
    }

    private IEnumerator PopOutScaleRoutine(Transform targetTransform)
    {
        if (targetTransform == null) yield break;

        Vector3 finalScale = targetTransform.localScale;
        targetTransform.localScale = finalScale * 0.4f;

        float duration = 0.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (targetTransform == null) yield break;

            elapsed += Time.deltaTime;
            float progress = elapsed / duration;
            float easeOut = Mathf.Sin(progress * Mathf.PI * 0.5f);

            targetTransform.localScale = Vector3.Lerp(finalScale * 0.4f, finalScale, easeOut);
            yield return null;
        }

        if (targetTransform != null) targetTransform.localScale = finalScale;
    }

    // ============================================
    // ★追加: 弾や爆風との衝突判定（味方の弾もキャッチする）
    // ============================================
    private void OnTriggerEnter(Collider other)
    {
        CheckHit(other.gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        CheckHit(collision.gameObject);
    }

    // ============================================
    // ★修正: 弾や爆風との衝突判定（味方の弾もキャッチし、HPが尽きたら即座に壊れる）
    // ============================================
    private void CheckHit(GameObject hitObj)
    {
        if (_isProcessed) return;

        // ★修正: TankStatus.TakeDamage を経由せず、直接箱のHPを減らす
        if (hitObj.CompareTag("Shell"))
        {
            ShellController shell = hitObj.GetComponent<ShellController>();
            if (shell != null)
            {
                int dmg = shell.shellData != null ? shell.shellData.damage : 10;
                _currentBoxHp -= dmg; // 直接HPを減らす
                shell.TriggerExplosionReaction();
            }
        }
        else if (hitObj.CompareTag("Explosion") || hitObj.layer == LayerMask.NameToLayer("Explode"))
        {
            _currentBoxHp -= 30; // 爆風なら固定ダメージで直接減らす
        }

        if (_currentBoxHp <= 0)
        {
            BreakBox();
        }
    }
}