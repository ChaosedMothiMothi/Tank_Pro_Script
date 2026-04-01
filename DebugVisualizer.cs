using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class DebugVisualizer : MonoBehaviour
{
    public static DebugVisualizer Instance { get; private set; }

    [Header("Debug Settings")]
    public bool showExplosionRadius = false;
    public bool showAimLines = false;

    [SerializeField] private Material debugSphereMaterial;
    [SerializeField] private Material debugLineMaterial;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // マテリアル自動生成（未設定時）
        if (debugSphereMaterial == null)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");
            debugSphereMaterial = new Material(shader);
            debugSphereMaterial.color = new Color(1f, 0f, 0f, 0.3f);
            debugSphereMaterial.SetFloat("_Mode", 3); // Transparent
            debugSphereMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            debugSphereMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            debugSphereMaterial.SetInt("_ZWrite", 0);
            debugSphereMaterial.DisableKeyword("_ALPHATEST_ON");
            debugSphereMaterial.EnableKeyword("_ALPHABLEND_ON");
            debugSphereMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            debugSphereMaterial.renderQueue = 3000;
        }

        if (debugLineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            debugLineMaterial = new Material(shader);
            debugLineMaterial.color = Color.magenta;
        }
    }

    private void Update()
    {
        // Eキー: 爆発範囲
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            showExplosionRadius = !showExplosionRadius;
            // Debug.Log($"<color=orange>[Debug]</color> 爆発範囲表示: {showExplosionRadius}");
        }

        // Lキー: 射線表示
        if (Keyboard.current.lKey.wasPressedThisFrame)
        {
            showAimLines = !showAimLines;
            // Debug.Log($"<color=magenta>[Debug]</color> 射撃予測線表示: {showAimLines}");
        }
    }

    // --- 爆発範囲可視化 ---

    /// <summary>
    /// 名前を CreateVisualizer に戻しました
    /// </summary>
    public void CreateVisualizer(Transform target, float radius)
    {
        if (target == null) return;

        GameObject viz = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        viz.name = "DebugExplosionRadius";
        viz.transform.SetParent(target);
        viz.transform.localPosition = Vector3.zero;
        viz.transform.localRotation = Quaternion.identity;

        // 親スケール補正（正球にする）
        Vector3 parentScale = target.lossyScale;
        // ゼロ除算回避
        if (Mathf.Abs(parentScale.x) > 0.001f && Mathf.Abs(parentScale.y) > 0.001f && Mathf.Abs(parentScale.z) > 0.001f)
        {
            float diameter = radius * 2.0f;
            viz.transform.localScale = new Vector3(diameter / parentScale.x, diameter / parentScale.y, diameter / parentScale.z);
        }

        Destroy(viz.GetComponent<Collider>());
        var renderer = viz.GetComponent<Renderer>();
        if (renderer != null) renderer.material = debugSphereMaterial;

        // 制御スクリプト追加
        viz.AddComponent<DebugRadiusController>();
    }

    // --- 射線可視化 (LineRenderer) ---

    public void DrawTrajectoryLine(LineRenderer lineRenderer, Vector3 firePointPos, Vector3 firePointDir, int maxBounces)
    {
        if (lineRenderer == null) return;

        if (!showAimLines)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;
        // マテリアル設定はAwake等で済ませているなら毎フレーム呼ばなくてOKですが、念のため
        if (lineRenderer.sharedMaterial != debugLineMaterial) lineRenderer.material = debugLineMaterial;

        List<Vector3> points = new List<Vector3>();
        points.Add(firePointPos);

        Vector3 currentPos = firePointPos;
        Vector3 currentDir = firePointDir;
        float segmentLength = 100f; // ★射線を長くする (50 -> 100)

        int wallLayerMask = LayerMask.GetMask("Wall"); // レイヤーマスクで取得

        for (int i = 0; i <= maxBounces; i++)
        {
            // Raycastで判定
            if (Physics.Raycast(currentPos, currentDir, out RaycastHit hit, segmentLength))
            {
                points.Add(hit.point);

                // 戦車に当たったらそこで止める
                if (hit.collider.GetComponentInParent<TankStatus>() != null) break;

                // 壁なら反射
                if (((1 << hit.collider.gameObject.layer) & wallLayerMask) != 0)
                {
                    currentPos = hit.point + hit.normal * 0.01f;
                    currentDir = Vector3.Reflect(currentDir, hit.normal);
                }
                else
                {
                    // 壁以外（障害物など）ならそこで止める
                    break;
                }
            }
            else
            {
                // 何にも当たらなければ遠くまで伸ばして終了
                points.Add(currentPos + currentDir * segmentLength);
                break;
            }
        }

        lineRenderer.positionCount = points.Count;
        lineRenderer.SetPositions(points.ToArray());
    }
}

    // --- 制御用クラス (同一ファイル内に定義) ---
    public class DebugRadiusController : MonoBehaviour
{
    private Renderer _renderer;
    private void Awake() => _renderer = GetComponent<Renderer>();
    private void Update()
    {
        if (DebugVisualizer.Instance == null) return;
        _renderer.enabled = DebugVisualizer.Instance.showExplosionRadius;
    }
}