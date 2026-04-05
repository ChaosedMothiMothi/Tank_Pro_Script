using UnityEngine;

[CreateAssetMenu(fileName = "NewBuffStepData", menuName = "Game/Buff Step Data")]
public class BuffStepData : ScriptableObject
{
    [Header("各レベル到達時の【ボーナス合計値】を設定します")]
    [Tooltip("要素数6 (Lv0〜Lv5) にしてください。Lv0 は常に 0 です。")]
    public int[] maxAmmoBonus = { 0, 1, 2, 3, 4, 5 };
    public int[] bounceBonus = { 0, 1, 2, 3, 4, 5 };
    public int[] mineLimitBonus = { 0, 1, 2, 3, 4, 5 };

    public float[] moveSpeedBonus = { 0f, 1.5f, 3.5f, 6.0f, 9.0f, 13.0f };
    public float[] rotationSpeedBonus = { 0f, 20f, 45f, 75f, 110f, 150f };
    public float[] shellSpeedBonus = { 0f, 5f, 12f, 20f, 30f, 45f };

    [Header("威力系 (上限10)")]
    [Tooltip("要素数11 (Lv0〜Lv10) にしてください。")]
    public int[] shellDamageBonus = { 0, 10, 25, 45, 70, 100, 135, 175, 220, 270, 330 };
    public int[] mineDamageBonus = { 0, 20, 50, 90, 140, 200, 270, 350, 440, 540, 650 };
}