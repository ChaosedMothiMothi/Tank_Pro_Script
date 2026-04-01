using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// どんな複雑なマテリアルでも強制的に「真っ赤」にして点滅させるクラス
/// マテリアルそのものを一瞬差し替えるため、確実に視認できます。
/// </summary>
public class MaterialFlasher : MonoBehaviour
{
    public static MaterialFlasher Instance { get; private set; }

    [Header("Flash Settings")]
    [SerializeField] private Color flashColor = new Color(1f, 0f, 0f, 1f); // 真っ赤
    [SerializeField] private float flashDuration = 0.1f;

    // 点滅用の一時的なマテリアル
    private Material _flashMaterial;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // メモリ上で「光る赤色マテリアル」を生成しておく
        // Unlit/Color シェーダーを使うことで、ライトの影響を受けずに発色します
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Diffuse"); // フェイルセーフ

        _flashMaterial = new Material(shader);
        _flashMaterial.color = flashColor;
    }

    /// <summary>
    /// 指定されたRendererのマテリアルを一時的に差し替えて光らせる
    /// </summary>
    public void Flash(Renderer targetRenderer)
    {
        if (targetRenderer == null) return;
        StartCoroutine(FlashRoutine(targetRenderer));
    }

    private IEnumerator FlashRoutine(Renderer renderer)
    {
        if (renderer == null) yield break;

        // 1. 元のマテリアル配列を保存
        Material[] originalMaterials = renderer.sharedMaterials;

        // 2. 「全てのマテリアル」を赤色マテリアルに差し替える配列を作る
        // (弱点部位が複数のマテリアルで構成されていても全体を赤くするため)
        Material[] flashMaterials = new Material[originalMaterials.Length];
        for (int i = 0; i < flashMaterials.Length; i++)
        {
            flashMaterials[i] = _flashMaterial;
        }

        // 3. 差し替え実行
        renderer.materials = flashMaterials;

        // 4. 待機
        yield return new WaitForSeconds(flashDuration);

        // 5. 元に戻す（オブジェクトが生きていれば）
        if (renderer != null)
        {
            renderer.sharedMaterials = originalMaterials;
        }
    }
}