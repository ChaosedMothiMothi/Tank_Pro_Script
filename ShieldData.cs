using UnityEngine;

[CreateAssetMenu(fileName = "NewShieldData", menuName = "Tank/ShieldData")]
public class ShieldData : ScriptableObject
{
    [Header("Basic Settings")]
    public string shieldName = "Standard Shield";
    public GameObject prefab; // シールドの見た目とコライダーを持つPrefab

    [Header("Stats")]
    public int maxHp = 5;

    [Tooltip("装備時の速度低下量（固定値）。例: 1.5 なら速度が 5.0 -> 3.5 になる")]
    public float speedPenalty = 1.5f;
}