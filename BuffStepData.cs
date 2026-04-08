using UnityEngine;

[CreateAssetMenu(fileName = "NewBuffStepData", menuName = "Game/Buff Step Data")]
public class BuffStepData : ScriptableObject
{
    [Header("各レベル到達時の【ボーナス合計値】を設定します")]
    [Tooltip("要素数6 (Lv0〜Lv5) にしてください。Lv0 は常に 0 です。")]
    public int[] maxAmmoBonus = { 0, 1, 2, 2, 2, 2 };
    public int[] bounceBonus = { 0, 1, 2, 3, 4, 5 };
    public int[] mineLimitBonus = { 0, 1, 2, 3, 4, 5 };

    public float[] moveSpeedBonus = { 0f, 7.5f, 12.5f, 15.0f, 17.5f, 20.0f };
    public float[] rotationSpeedBonus = { 0f, 60f, 120f, 180f, 240f, 300f };
    public float[] shellSpeedBonus = { 0f, 7.5f, 12.5f, 15f, 17.5f, 20f };

    [Header("威力系 (上限10)")]
    [Tooltip("要素数11 (Lv0〜Lv10) にしてください。")]
    public int[] shellDamageBonus = { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50 };
    public int[] mineDamageBonus = { 0, 20, 35, 50, 65, 75, 80, 90, 95, 100, 120 };
}