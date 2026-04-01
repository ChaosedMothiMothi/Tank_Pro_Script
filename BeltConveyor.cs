using UnityEngine;
using System.Collections.Generic;

public class BeltConveyor : MonoBehaviour
{
    [Header("コンベア設定")]
    [Tooltip("コンベアの移動速度（プラスで右、マイナスで左）")]
    public float conveyorSpeed = 3.0f;

    [Tooltip("テクスチャのスクロール速度倍率")]
    public float visualScrollMultiplier = 0.2f;

    private Renderer _renderer;
    private Material _material;

    // 重複防止用
    private static HashSet<Rigidbody> _movedRigidbodies = new HashSet<Rigidbody>();
    private static int _lastFrameCount = -1;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        if (_renderer != null) _material = _renderer.material;
    }

    private void FixedUpdate()
    {
        // フレームが変わったらリストをリセット
        if (_lastFrameCount != Time.frameCount)
        {
            _movedRigidbodies.Clear();
            _lastFrameCount = Time.frameCount;
        }
    }

    private void Update()
    {
        // テクスチャのアニメーション（X軸スクロール）
        if (_material != null)
        {
            float offset = Time.time * conveyorSpeed * visualScrollMultiplier;
            // X軸方向（横）にスクロール
            Vector2 textureOffset = _material.mainTextureOffset;
            textureOffset.x = offset; // 矢印の向きに合わせて符号を調整してください
            _material.mainTextureOffset = textureOffset;
        }
    }

    // IsTrigger = true の場合、OnCollisionStayは呼ばれないため OnTriggerStay を使う
    private void OnTriggerStay(Collider other)
    {
        ApplyConveyorForce(other.attachedRigidbody);
    }

    // 念のため Collision も残しておく（IsTriggerにし忘れた場合用）
    private void OnCollisionStay(Collision collision)
    {
        ApplyConveyorForce(collision.rigidbody);
    }

    private void ApplyConveyorForce(Rigidbody targetRb)
    {
        // 1. 対象チェック
        if (targetRb == null || targetRb.isKinematic) return;

        // 2. 重複チェック
        if (_movedRigidbodies.Contains(targetRb)) return;

        // 3. 移動処理（X軸方向＝Right）
        // transform.right は赤矢印の方向です
        Vector3 movement = transform.right * conveyorSpeed * Time.fixedDeltaTime;

        // Y軸（高さ）には影響を与えないようにそのままの位置を使う
        Vector3 newPos = targetRb.position + movement;

        targetRb.MovePosition(newPos);

        // 4. 処理済みに登録
        _movedRigidbodies.Add(targetRb);
    }
}