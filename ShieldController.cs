using UnityEngine;

public class ShieldController : MonoBehaviour
{
    public ShieldData Data { get; private set; }
    private int _currentHp;
    private TankStatus _owner;
    private Renderer _renderer; // 追加: 描画コンポーネント取得用

    public void Init(TankStatus owner, ShieldData data)
    {
        _owner = owner;
        this.Data = data;
        _currentHp = data.maxHp;

        // 追加: 子供のレンダラーも含めて探すか、自身のみか。通常は自身。
        _renderer = GetComponent<Renderer>();
        if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
    }

    public void TakeShieldDamage(int damage)
    {
        _currentHp -= damage;

        // --- 追加: 赤く点滅 ---
        if (MaterialFlasher.Instance != null && _renderer != null)
        {
            MaterialFlasher.Instance.Flash(_renderer);
        }

        if (_currentHp <= 0)
        {
            BreakShield();
        }
    }

    private void BreakShield()
    {
        if (_owner != null) _owner.OnShieldBroken();
        Destroy(gameObject);
    }
}