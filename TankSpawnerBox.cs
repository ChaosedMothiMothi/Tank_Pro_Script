using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スポーン候補となる戦車のデータ
/// </summary>
[System.Serializable]
public class ReinforcementTankData
{
    [Tooltip("スポーンさせる戦車のPrefabを設定します。")]
    public GameObject tankPrefab;

    [Tooltip("この戦車が選ばれる確率の重みです。数値が大きいほど出やすくなります。")]
    public int weight = 10;

    [Tooltip("箱の中にいる間、箱からはみ出ないように非表示にするパーツの名前（Turret, Barrelなど）を指定します。")]
    public List<string> partsToHideNames;
}

/// <summary>
/// 開く箱の面ごとのデータ
/// </summary>
[System.Serializable]
public class BoxPanelData
{
    [Tooltip("開く面のTransform（壁オブジェクト）を指定します。")]
    public Transform panelTransform;

    [Tooltip("箱が開く際に、この面がどの方向にどれくらい回転するかを指定します。\n（例: 前に倒れる面なら X:90, 横に倒れる面なら Z:90 など）")]
    public Vector3 openRotation;
}

public class TankSpawnerBox : MonoBehaviour
{
    [Header("■ スポーン設定 (Spawn Settings)")]
    [Tooltip("スポーンする戦車の候補リストです。複数入れると重みに応じてランダムに選ばれます。")]
    [SerializeField] private List<ReinforcementTankData> spawnableTanks;

    [Tooltip("戦車が出現する位置と向きの基準となる空オブジェクトを指定します。")]
    [SerializeField] private Transform tankSpawnPoint;

    [Header("■ 時間設定 (Timing Settings)")]
    [Tooltip("設置されてから、箱が1倍のサイズに成長しきるまでの時間（秒）です。")]
    [SerializeField] private float growthDuration = 1.0f;

    [Tooltip("設置されてから、箱がパカッと開き始めるまでの待機時間（秒）です。")]
    [SerializeField] private float openDelay = 2.5f;

    [Tooltip("箱が開いて戦車が起動した後、この空箱自体が消滅するまでの時間（秒）です。")]
    [SerializeField] private float boxLifetimeAfterOpen = 1.5f;

    [Header("■ アニメーション設定 (Animation Settings)")]
    [Tooltip("花が開くように展開する、箱の各面のリストです。面ごとに倒れる角度を設定してください。")]
    [SerializeField] private List<BoxPanelData> sidePanels;

    [Tooltip("面が開くアニメーションの再生速度です。数値が大きいほど素早くバタンと開きます。")]
    [SerializeField] private float panelOpenSpeed = 4.0f;

    [Header("■ 破壊エフェクト (Destruction)")]
    [Tooltip("展開前に敵の弾が当たって壊れた際に再生する爆発エフェクトのPrefabを指定します。")]
    [SerializeField] private GameObject destructionEffectPrefab;

    // --- 内部処理用の変数 ---
    private TeamType _alliedTeam;                     // 味方となるチーム情報
    private GameObject _spawnedTankInstance;          // 生成された戦車の実体
    private List<GameObject> _hiddenParts = new List<GameObject>(); // 一時的に隠しているパーツのリスト
    private bool _isOpened = false;                   // 箱がすでに開いたかどうかのフラグ
    private bool _isDestroyed = false;                // 破壊済みかどうかのフラグ
    private TankStatus _ownerStatus;                  // この箱を設置した主（プレイヤーなど）のステータス

    /// <summary>
    /// 箱が設置された瞬間に、TankController等から呼ばれる初期化処理です。
    /// </summary>
    public void Init(TankStatus owner, TeamType team)
    {
        _ownerStatus = owner;
        _alliedTeam = team;

        // 設置直後はサイズを0.1倍にして小さく見せる
        transform.localScale = Vector3.one * 0.1f;

        // サイズが大きくなるアニメーションと、戦車を準備する処理を同時に開始する
        StartCoroutine(GrowthRoutine());
        StartCoroutine(SpawnRoutine());
    }

    /// <summary>
    /// 箱が徐々に1倍のサイズに成長するアニメーション処理です。
    /// </summary>
    private IEnumerator GrowthRoutine()
    {
        float timer = 0f;
        while (timer < growthDuration)
        {
            timer += Time.deltaTime;
            // Lerpを使って、現在の時間に合わせて0.1倍から1.0倍へ滑らかに拡大させる
            transform.localScale = Vector3.Lerp(Vector3.one * 0.1f, Vector3.one, timer / growthDuration);
            yield return null; // 次のフレームまで待機
        }
        // 最後にズレを無くすため、きっちり1倍にする
        transform.localScale = Vector3.one;
    }

    /// <summary>
    /// 戦車を生成し、一定時間待ってから箱を開いて戦闘に参加させるメインシーケンスです。
    /// </summary>
    private IEnumerator SpawnRoutine()
    {
        // 1. 重み付けを利用して、リストの中からスポーンさせる戦車を1つ抽選する
        ReinforcementTankData selectedData = GetRandomTankData();
        if (selectedData != null && selectedData.tankPrefab != null)
        {
            // 2. 抽選された戦車を生成し、箱と一緒に動くように子オブジェクトに設定する
            _spawnedTankInstance = Instantiate(selectedData.tankPrefab, tankSpawnPoint.position, tankSpawnPoint.rotation);
            _spawnedTankInstance.transform.SetParent(tankSpawnPoint);

            // 3. 味方として戦ってもらうため、設置者と同じチームを設定する
            TankStatus status = _spawnedTankInstance.GetComponent<TankStatus>();
            if (status != null) status.team = _alliedTeam;

            // 4. 箱の中にいる間に勝手に動いたり撃ったりしないよう、AIスクリプトを一時的に停止する
            var ai = _spawnedTankInstance.GetComponent<EnemyTankController>();
            if (ai != null) ai.enabled = false;

            // 5. 砲塔など、箱からはみ出してしまうパーツを検索して非表示にする
            HideSpecificParts(_spawnedTankInstance, selectedData.partsToHideNames);
        }

        // --- 箱が開くまで待機 ---
        yield return new WaitForSeconds(openDelay);

        // 待機中に敵の弾で壊されていたら、これ以降の処理（開く処理）はキャンセルする
        if (_isDestroyed) yield break;

        // --- 箱の展開開始 ---
        _isOpened = true;
        StartCoroutine(OpenPanelsRoutine()); // 面をパタパタと開くアニメーション開始

        // 6. 戦車を箱から解放し、戦闘準備をさせる
        if (_spawnedTankInstance != null)
        {
            // 親子関係を解除し、空箱が消えても戦車が一緒に消えないようにする
            _spawnedTankInstance.transform.SetParent(null);

            // 隠していたパーツ（砲塔など）を再表示する
            foreach (var part in _hiddenParts)
            {
                if (part != null) part.SetActive(true);
            }

            // 停止していたAIを起動し、戦闘を開始させる
            var ai = _spawnedTankInstance.GetComponent<EnemyTankController>();
            if (ai != null) ai.enabled = true;
        }

        // --- 空箱の消滅待ち ---
        yield return new WaitForSeconds(boxLifetimeAfterOpen);

        // 主の「設置中の地雷（箱）カウント」を減らす（上限管理のため）
        if (_ownerStatus != null) _ownerStatus.ActiveMineCount--;

        // 役目を終えた空箱をシーンから削除する
        Destroy(gameObject);
    }

    /// <summary>
    /// 登録された複数の面を、指定されたそれぞれの角度へ向かって滑らかに開く処理です。
    /// </summary>
    private IEnumerator OpenPanelsRoutine()
    {
        float t = 0;
        List<Quaternion> startRots = new List<Quaternion>();
        List<Quaternion> targetRots = new List<Quaternion>();

        // 各面の初期の角度と、目標となる角度（開いた状態）を計算して保存しておく
        foreach (var p in sidePanels)
        {
            if (p.panelTransform == null) continue;
            startRots.Add(p.panelTransform.localRotation);
            // 現在の回転に対して、インスペクターで指定した角度（openRotation）を加算する
            targetRots.Add(p.panelTransform.localRotation * Quaternion.Euler(p.openRotation));
        }

        // t が 1.0 (100%) になるまで回転アニメーションを続ける
        while (t < 1.0f)
        {
            t += Time.deltaTime * panelOpenSpeed;
            for (int i = 0; i < sidePanels.Count; i++)
            {
                if (sidePanels[i].panelTransform == null) continue;
                // 初期角度から目標角度へ、t の割合に応じて回転させる
                sidePanels[i].panelTransform.localRotation = Quaternion.Lerp(startRots[i], targetRots[i], t);
            }
            yield return null;
        }
    }

    /// <summary>
    /// 他のオブジェクトのコライダーがこの箱に触れたときの処理です（弾の被弾判定など）。
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        // すでに破壊済み、あるいは展開済み（開いた後）ならダメージ判定は無視する
        if (_isDestroyed || _isOpened) return;

        // ぶつかってきたオブジェクトの親要素などに「ShellController（弾のスクリプト）」が含まれているか確認
        if (other.GetComponentInParent<ShellController>() != null)
        {
            DestroyBoxAndCancelSpawn(); // 弾なら箱を破壊する
        }
    }

    /// <summary>
    /// 箱が弾で破壊された際の処理です。中の戦車ごと削除します。
    /// </summary>
    private void DestroyBoxAndCancelSpawn()
    {
        _isDestroyed = true;

        // 生成準備中だった戦車があれば一緒に破壊する
        if (_spawnedTankInstance != null) Destroy(_spawnedTankInstance);

        // 爆発エフェクトが設定されていれば再生する
        if (destructionEffectPrefab != null) Instantiate(destructionEffectPrefab, transform.position, Quaternion.identity);

        // 主の設置カウントを減らす
        if (_ownerStatus != null) _ownerStatus.ActiveMineCount--;

        // 自身を削除する
        Destroy(gameObject);
    }

    /// <summary>
    /// 指定された名前のパーツ（子オブジェクト）を探し出して非表示にします。
    /// </summary>
    private void HideSpecificParts(GameObject tank, List<string> partNames)
    {
        _hiddenParts.Clear();
        if (partNames == null || partNames.Count == 0) return;

        // 戦車の中にあるすべての子オブジェクト（非アクティブ含む）を取得
        Transform[] allChildren = tank.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in allChildren)
        {
            // 名前が指定されたリストの中に含まれていれば非表示にする
            if (partNames.Contains(child.name))
            {
                _hiddenParts.Add(child.gameObject);
                child.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// 確率の重み（Weight）を考慮して、リストからランダムに戦車を1つ選びます。
    /// </summary>
    private ReinforcementTankData GetRandomTankData()
    {
        if (spawnableTanks == null || spawnableTanks.Count == 0) return null;

        int totalWeight = 0;
        // まず、全員の重みの合計値を計算する
        foreach (var d in spawnableTanks) totalWeight += d.weight;

        // 0 ～ 合計値 の間でランダムな数値を出す
        int rand = Random.Range(0, totalWeight);
        int current = 0;

        // リストを順番に確認し、ランダム値がどこに当てはまるかを探す
        foreach (var d in spawnableTanks)
        {
            current += d.weight;
            if (rand < current) return d;
        }

        // 予期せぬエラー回避のため、念のため最後の要素を返す
        return spawnableTanks[spawnableTanks.Count - 1];
    }
}