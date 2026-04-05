using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class TankSpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        [Tooltip("出現させる戦車のPrefab")]
        public GameObject prefab;
        [Tooltip("出現確率の重み（数字が大きいほど出やすい）")]
        public int weight = 10;
    }

    [Header("Spawn Settings")]
    [Tooltip("出現する戦車リスト（重み付き）")]
    public List<SpawnEntry> tankPrefabs;

    [Tooltip("スポーン間隔（秒）")]
    public float spawnInterval = 5.0f;

    [Tooltip("スポーンさせる戦車のチーム")]
    public TeamType spawnTeam ;

    [Header("Path Settings")]
    [Tooltip("移動経路となるオブジェクト（順番に通過します）")]
    public Transform[] pathPoints;

    [Tooltip("パス移動中の速度")]
    public float forcedMoveSpeed = 5.0f;

    [Header("Limits")]
    [Tooltip("このスポーン地点からの最大出現数（0の場合は無限）")]
    public int maxTotalSpawns = 0;

    [Tooltip("画面上の最大戦車数（これ以上いる場合はスポーン待機）")]
    public int maxActiveTanks = 10;

    [Header("Spawner Health")]
    [Tooltip("スポーン地点のHP（0の場合は無敵）")]
    public int hp = 200;

    [Tooltip("ダメージを受けた際の明滅用レンダラー")]
    public Renderer targetRenderer;

    // --- 内部変数 ---
    private int _spawnedCount = 0;
    private float _timer = 0f;
    private bool _isDestroyed = false;
    private Coroutine _flashCoroutine;
    private Collider _garageCollider; // ガレージ自身のコライダー

    private void Start()
    {
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        _garageCollider = GetComponent<Collider>(); // コライダー取得
    }

    private void Update()
    {
        if (_isDestroyed) return;

        // ★追加: ゲーム開始前、または終了時にはスポーンのタイマーを止める
        if (GameManager.Instance != null && (!GameManager.Instance.IsGameStarted || GameManager.Instance.IsGameFinished()))
        {
            return; // 何もせず待機
        }

        if (maxTotalSpawns == 0 || _spawnedCount < maxTotalSpawns)
        {
            _timer += Time.deltaTime;

            if (_timer >= spawnInterval)
            {
                if (CheckCanSpawn())
                {
                    SpawnTank();
                    _timer = 0f;
                }
            }
        }
    }

    private bool CheckCanSpawn()
    {
        int currentTankCount = FindObjectsByType<TankStatus>(FindObjectsSortMode.None)
            .Count(t => !t.IsDead);
        return currentTankCount < maxActiveTanks;
    }

    private void SpawnTank()
    {
        GameObject prefabToSpawn = GetRandomPrefab();
        if (prefabToSpawn == null) return;

        GameObject newTank = Instantiate(prefabToSpawn, transform.position, transform.rotation);

        // ★追加: ガレージとスポーン戦車の衝突を無視する（すり抜け処理）
        if (_garageCollider != null)
        {
            Collider[] tankColliders = newTank.GetComponentsInChildren<Collider>();
            foreach (var col in tankColliders)
            {
                Physics.IgnoreCollision(_garageCollider, col, true);
            }
        }

        TankStatus status = newTank.GetComponent<TankStatus>();
        if (status != null) status.SetTeam(spawnTeam);

        // コントローラーの一時無効化
        var enemyCtrl = newTank.GetComponent<EnemyTankController>();
        if (enemyCtrl != null)
        {
            enemyCtrl.enabled = false;
            // ★追加: スポーナーから無限湧きする敵はパーツを落とさないように強制上書きする
            enemyCtrl.SetDropPartsCount(0);
        }

        var navMeshAgent = newTank.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navMeshAgent != null) navMeshAgent.enabled = false;

        // パス移動設定
        if (pathPoints != null && pathPoints.Length > 0)
        {
            var mover = newTank.AddComponent<SpawnPathMover>();
            mover.Initialize(pathPoints, forcedMoveSpeed, () =>
            {
                if (navMeshAgent != null)
                {
                    navMeshAgent.enabled = true;
                    navMeshAgent.Warp(newTank.transform.position);
                }
                if (enemyCtrl != null) enemyCtrl.enabled = true;
            });
        }
        else
        {
            if (navMeshAgent != null) navMeshAgent.enabled = true;
            if (enemyCtrl != null) enemyCtrl.enabled = true;
        }

        _spawnedCount++;
    }

    private GameObject GetRandomPrefab()
    {
        if (tankPrefabs == null || tankPrefabs.Count == 0) return null;
        int totalWeight = tankPrefabs.Sum(x => x.weight);
        int randomValue = Random.Range(0, totalWeight);
        int currentWeight = 0;
        foreach (var entry in tankPrefabs)
        {
            currentWeight += entry.weight;
            if (randomValue < currentWeight) return entry.prefab;
        }
        return tankPrefabs[0].prefab;
    }

    // --- ダメージ処理 (外部呼び出し用) ---
    public void TakeDamage(int damage)
    {
        if (hp <= 0 || _isDestroyed) return;

        hp -= damage;

        if (targetRenderer != null)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashRoutine());
        }

        if (hp <= 0) Die();
    }

    private void Die()
    {
        _isDestroyed = true;
        if (EffectManager.Instance != null) EffectManager.Instance.PlayExplosion(transform.position);
        Destroy(gameObject);
    }

    private IEnumerator FlashRoutine()
    {
        if (targetRenderer == null) yield break;
        Color originalColor = targetRenderer.material.color;
        targetRenderer.material.color = Color.red;
        yield return new WaitForSeconds(0.1f);
        targetRenderer.material.color = originalColor;
        _flashCoroutine = null;
    }

    // 弾や地雷の爆風判定用（Garageタグがついている前提で、ShellController等が接触した際に呼ばれる）
    private void OnTriggerEnter(Collider other)
    {
        if (hp <= 0 || _isDestroyed) return;

        if (other.CompareTag("Shell"))
        {
            var shell = other.GetComponent<ShellController>();
            if (shell != null)
            {
                int dmg = (shell.shellData != null) ? shell.shellData.damage : 20;
                TakeDamage(dmg);
                // 弾の消滅は弾側の処理に任せる
            }
        }
    }
}