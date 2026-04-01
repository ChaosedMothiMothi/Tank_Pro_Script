using UnityEngine;
using System.Collections;

/// <summary>
/// 死亡時に飛び散ったパーツを時間差で爆発・消滅させるクラス
/// </summary>
public class DebrisExploder : MonoBehaviour
{
    private GameObject _explosionPrefab;
    private bool _hasExploded = false;

    public void Init(GameObject explosionPrefab, float delay)
    {
        _explosionPrefab = explosionPrefab;
        StartCoroutine(ExplodeSequence(delay));
    }

    private IEnumerator ExplodeSequence(float delay)
    {
        // 指定時間待機
        yield return new WaitForSeconds(delay);

        if (_hasExploded) yield break;
        _hasExploded = true;

        // エフェクト生成
        if (_explosionPrefab != null)
        {
            Instantiate(_explosionPrefab, transform.position, Quaternion.identity);
        }

        // パーツ削除
        Destroy(gameObject);
    }
}