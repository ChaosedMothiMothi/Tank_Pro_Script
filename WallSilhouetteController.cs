using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class WallSilhouetteController : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("SilhouetteMat (Custom/SilhouetteAlways) を割り当ててください")]
    [SerializeField] private Material silhouetteMaterialBase;

    [Tooltip("シルエットの色")]
    [SerializeField] private Color silhouetteColor = new Color(1f, 0f, 0f, 0.5f);

    [Header("Detection")]
    [Tooltip("遮蔽物とみなすレイヤー")]
    [SerializeField] private LayerMask obstacleLayerMask;

    [Tooltip("タグで判定する場合はチェック（推奨）")]
    [SerializeField] private bool useTagCheck = true;
    [SerializeField] private string obstacleTag = "Wall";

    [Tooltip("カメラが壁に埋まっている場合の対策距離（手前の壁対策）")]
    [SerializeField] private float cameraOffsetDistance = 10.0f;

    // 内部変数
    private Renderer[] _renderers;
    private bool _isSilhouetted = false;
    private Transform _cameraTransform;
    private Material _mySilhouetteInstance;
    private string _silhouetteShaderName;

    private void Awake()
    {
        // メッシュレンダラーとスキンメッシュレンダラーを取得
        var meshRenderers = GetComponentsInChildren<MeshRenderer>();
        var skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>();

        List<Renderer> rList = new List<Renderer>();
        // ★修正: デバッグ用オブジェクトなどを除外してリストに追加
        foreach (var r in meshRenderers)
        {
            if (ShouldIgnoreRenderer(r)) continue;
            rList.Add(r);
        }
        foreach (var r in skinnedRenderers)
        {
            if (ShouldIgnoreRenderer(r)) continue;
            rList.Add(r);
        }

        _renderers = rList.ToArray();

        if (Camera.main != null)
        {
            _cameraTransform = Camera.main.transform;
        }

        // マテリアルインスタンスの作成
        if (silhouetteMaterialBase != null)
        {
            _mySilhouetteInstance = new Material(silhouetteMaterialBase);
            _mySilhouetteInstance.SetColor("_SilhouetteColor", silhouetteColor);
            _mySilhouetteInstance.name = "Silhouette_Overlay_Instance";
            _silhouetteShaderName = silhouetteMaterialBase.shader.name;
        }

        // LayerMask初期設定
        if (obstacleLayerMask.value == 0)
        {
            obstacleLayerMask = Physics.AllLayers;
        }
    }

    private void Update()
    {
        if (_cameraTransform == null || _mySilhouetteInstance == null) return;

        // ★修正: Rayの始点をカメラよりさらに後ろにずらす
        // これにより、カメラが「手前の壁」の内側にあっても、壁を検知できる
        Vector3 camPos = _cameraTransform.position;
        Vector3 targetPos = transform.position; // 戦車の位置

        // カメラの背後方向へオフセット
        Vector3 directionToCamera = (camPos - targetPos).normalized;
        // 平行投影の場合は -transform.forward がカメラの背面方向
        Vector3 backOffset = -_cameraTransform.forward * cameraOffsetDistance;

        // 始点をカメラ位置よりさらに手前（画面手前側）に引く
        Vector3 rayStart = camPos + backOffset;

        // ターゲットまでのベクトル
        Vector3 rayDir = targetPos - rayStart;
        float rayDist = rayDir.magnitude;

        // 全ヒット取得
        RaycastHit[] hits = Physics.RaycastAll(rayStart, rayDir.normalized, rayDist, obstacleLayerMask);

        bool wallFound = false;

        foreach (var hit in hits)
        {
            // 自分自身は無視
            if (hit.transform.IsChildOf(transform)) continue;

            // タグ判定
            if (useTagCheck)
            {
                if (hit.collider.CompareTag(obstacleTag))
                {
                    wallFound = true;
                    // デバッグ用: 何が壁として検知されたか
                    // Debug.Log($"Wall detected: {hit.collider.name}");
                    break;
                }
            }
            else
            {
                // レイヤー判定のみ（タグチェックOFFの場合）
                wallFound = true;
                break;
            }
        }

        // 状態変化時のみ適用
        if (wallFound != _isSilhouetted)
        {
            ApplySilhouette(wallFound);
        }
    }

    private void ApplySilhouette(bool enable)
    {
        _isSilhouetted = enable;

        foreach (var r in _renderers)
        {
            if (r == null) continue;

            var mats = r.materials; // コピーを取得
            var matList = new List<Material>(mats);

            if (enable)
            {
                // ★修正: 名前やシェーダー名で重複を厳密にチェック
                bool alreadyHas = false;
                foreach (var m in matList)
                {
                    // 名前またはシェーダー名が一致したら「既に持っている」とみなす
                    if (m.name.Contains(_mySilhouetteInstance.name) || m.shader.name == _silhouetteShaderName)
                    {
                        alreadyHas = true;
                        break;
                    }
                }

                if (!alreadyHas)
                {
                    matList.Add(_mySilhouetteInstance);
                    r.materials = matList.ToArray();
                }
            }
            else
            {
                // ★修正: 削除時も確実に除去
                // 逆順ループで安全に削除
                bool removed = false;
                for (int i = matList.Count - 1; i >= 0; i--)
                {
                    if (matList[i].name.Contains(_mySilhouetteInstance.name) || matList[i].shader.name == _silhouetteShaderName)
                    {
                        matList.RemoveAt(i);
                        removed = true;
                    }
                }

                if (removed)
                {
                    r.materials = matList.ToArray();
                }
            }
        }
    }

    public void SetColor(Color c)
    {
        silhouetteColor = c;
        if (_mySilhouetteInstance != null)
        {
            _mySilhouetteInstance.SetColor("_SilhouetteColor", silhouetteColor);
        }
    }

    private void OnDestroy()
    {
        if (_mySilhouetteInstance != null)
        {
            Destroy(_mySilhouetteInstance);
        }
    }


    // ★追加: 無視すべきレンダラーの判定ロジック
    private bool ShouldIgnoreRenderer(Renderer r)
    {
        // 1. デバッグ表示用のオブジェクトを除外
        // DebugVisualizerで生成される球体には "Debug" や "Visualizer" といった名前が含まれていることが多い
        // または、特定の名前 "ExplosionRadiusVisualizer" など
        if (r.gameObject.name.Contains("Visualizer") || r.gameObject.name.Contains("Debug")) return true;

        // 2. パーティクルやUIは念のため除外（型判定で既に弾いているが念のため）
        if (r is ParticleSystemRenderer || r is LineRenderer) return true;

        // 3. 影（Shadow）用のオブジェクトなどがあれば除外
        if (r.gameObject.name.Contains("Shadow")) return true;

        return false;
    }

}