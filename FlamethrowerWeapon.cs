using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 弾プレハブを使わない専用火炎放射武装。
/// 照準・ダメージ・見た目は aimTransform の向き（forward）に合わせる。
/// </summary>
public class FlamethrowerWeapon : MonoBehaviour
{
    [Header("照準")]
    [Tooltip("火炎の起点と向き（未設定時はこのオブジェクト自身）")]
    public Transform aimTransform;
    [Tooltip("ノズルモデルの向きがずれている場合のY軸補正（度）。90度ずれているときは -90 または 90 を試す")]
    public float aimYawOffset = 0f;

    [Header("火力")]
    public int damagePerTick = 3;
    public float tickInterval = 0.1f;
    public float range = 10f;
    public float radius = 1.2f;
    public float coneAngle = 50f;

    [Header("見た目（メッシュコーン）")]
    public Material flameMaterial;
    public Color flameColorNear = new Color(1f, 0.95f, 0.3f, 0.75f);
    public Color flameColorFar = new Color(1f, 0.35f, 0.05f, 0.15f);
    public int coneSegments = 16;
    public bool pulseVisual = true;

    private TankStatus _owner;
    private bool _isActive;
    private float _tickTimer;
    private float _visualPulseTime;

    private Transform _visualRoot;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _flameMesh;
    private Material _runtimeMaterial;

    public bool IsActive => _isActive;

    private Transform Aim => aimTransform != null ? aimTransform : transform;

    private void Awake()
    {
        if (aimTransform == null) aimTransform = transform;
        EnsureVisuals();
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
        if (_flameMesh != null) Destroy(_flameMesh);
    }

    public void BeginFlame(TankStatus owner)
    {
        _owner = owner;
        _isActive = true;
        _tickTimer = 0f;
        _visualPulseTime = 0f;

        EnsureVisuals();
        if (_meshRenderer != null) _meshRenderer.enabled = true;
        SyncVisualTransform();
        UpdateFlameVisual();
    }

    public void EndFlame()
    {
        _isActive = false;
        _owner = null;
        if (_meshRenderer != null) _meshRenderer.enabled = false;
    }

    private void Update()
    {
        if (!_isActive || _owner == null || _owner.IsDead) return;

        SyncVisualTransform();
        _visualPulseTime += Time.deltaTime;
        UpdateFlameVisual();

        _tickTimer -= Time.deltaTime;
        if (_tickTimer > 0f) return;
        _tickTimer = tickInterval;

        ApplyFlameDamage();
    }

    private void SyncVisualTransform()
    {
        if (_visualRoot == null) return;
        Transform aim = Aim;
        _visualRoot.position = aim.position;
        _visualRoot.rotation = aim.rotation;
    }

    private void EnsureVisuals()
    {
        if (_meshFilter != null) return;

        _visualRoot = new GameObject("FlameConeVisual").transform;
        _meshFilter = _visualRoot.gameObject.AddComponent<MeshFilter>();
        _meshRenderer = _visualRoot.gameObject.AddComponent<MeshRenderer>();
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;

        _flameMesh = new Mesh { name = "FlamethrowerCone" };
        _meshFilter.sharedMesh = _flameMesh;
        _meshRenderer.sharedMaterial = GetVisualMaterial();
        _meshRenderer.enabled = false;
    }

    private Material GetVisualMaterial()
    {
        if (flameMaterial != null) return flameMaterial;

        if (_runtimeMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            _runtimeMaterial = new Material(shader);
        }
        return _runtimeMaterial;
    }

    private void UpdateFlameVisual()
    {
        if (_flameMesh == null) return;

        float halfAngleRad = (coneAngle * 0.5f) * Mathf.Deg2Rad;
        float endRadius = Mathf.Tan(halfAngleRad) * range;
        if (pulseVisual)
        {
            float pulse = 1f + Mathf.Sin(_visualPulseTime * 12f) * 0.08f;
            endRadius *= pulse;
        }

        int seg = Mathf.Max(3, coneSegments);
        int vertCount = 1 + seg;
        Vector3[] vertices = new Vector3[vertCount];
        Color[] colors = new Color[vertCount];
        int[] triangles = new int[seg * 3];

        vertices[0] = Vector3.zero;
        colors[0] = flameColorNear;

        for (int i = 0; i < seg; i++)
        {
            float angle = (i / (float)seg) * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * endRadius;
            float y = Mathf.Sin(angle) * endRadius;
            vertices[i + 1] = new Vector3(x, y, range);
            colors[i + 1] = flameColorFar;

            int tri = i * 3;
            triangles[tri] = 0;
            triangles[tri + 1] = i + 1;
            triangles[tri + 2] = (i + 1) % seg + 1;
        }

        _flameMesh.Clear();
        _flameMesh.vertices = vertices;
        _flameMesh.colors = colors;
        _flameMesh.triangles = triangles;
        _flameMesh.RecalculateNormals();
        _flameMesh.RecalculateBounds();

        if (_meshRenderer != null && flameMaterial == null && _runtimeMaterial != null)
        {
            _meshRenderer.sharedMaterial = _runtimeMaterial;
        }
    }

    private void ApplyFlameDamage()
    {
        Transform aim = Aim;
        Vector3 origin = aim.position;
        Vector3 forward = aim.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) forward = aim.TransformDirection(Vector3.forward);
        forward.y = 0f;
        forward.Normalize();

        int steps = Mathf.Max(1, Mathf.CeilToInt(range / Mathf.Max(0.3f, radius)));
        HashSet<TankStatus> damaged = new HashSet<TankStatus>();

        for (int i = 0; i <= steps; i++)
        {
            float dist = (i / (float)steps) * range;
            Vector3 checkPos = origin + forward * dist;
            Collider[] hits = Physics.OverlapSphere(checkPos, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);

            foreach (Collider col in hits)
            {
                if (col == null) continue;
                if (!IsWithinCone(origin, forward, col.transform.position)) continue;

                TankStatus target = col.GetComponentInParent<TankStatus>();
                if (target == null || target.IsDead || target == _owner) continue;
                if (target.team == _owner.team) continue;

                if (damaged.Add(target))
                {
                    target.TakeDamage(damagePerTick, _owner);
                }
            }
        }
    }

    private bool IsWithinCone(Vector3 origin, Vector3 forward, Vector3 worldPos)
    {
        Vector3 toTarget = worldPos - origin;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f) return true;
        return Vector3.Angle(forward, toTarget) <= coneAngle * 0.5f;
    }
}
