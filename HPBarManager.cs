using UnityEngine;
using System.Collections.Generic;

public class HPBarManager : MonoBehaviour
{
    public static HPBarManager Instance;

    [Header("UI References")]
    public HPBarController bossHPBar;
    public GameObject genericHPBarPrefab;
    public Transform genericHPBarContainer;

    private Dictionary<object, HPBarController> _activeBars = new Dictionary<object, HPBarController>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (bossHPBar != null) bossHPBar.gameObject.SetActive(false);
    }

    // ==========================================
    // ★修正: プレハブ名ではなく、ScriptableObjectの「tankName」を使う
    // ==========================================
    public void RegisterTank(TankStatus tank)
    {
        // TankStatusData (ScriptableObject) から正式な名前を取得する。
        // もしデータがない、または名前が未設定ならプレハブ名をフォールバックとして使う。
        string displayName = tank.name;
        if (tank.GetData() != null && !string.IsNullOrEmpty(tank.GetData().tankName))
        {
            displayName = tank.GetData().tankName;
        }

        if (tank.isBoss && bossHPBar != null)
        {
            bossHPBar.gameObject.SetActive(true);
            bossHPBar.Init(displayName, tank.GetData().maxHp, null);
            _activeBars[tank] = bossHPBar;
        }
        else if (genericHPBarPrefab != null && genericHPBarContainer != null)
        {
            GameObject barObj = Instantiate(genericHPBarPrefab, genericHPBarContainer);
            HPBarController bar = barObj.GetComponent<HPBarController>();

            if (bar != null)
            {
                bar.Init(displayName, tank.GetData() != null ? tank.GetData().maxHp : 100, tank.transform);
                _activeBars[tank] = bar;
            }
            else
            {
                Debug.LogWarning("GenericHPBarPrefab に HPBarController がアタッチされていません！");
            }
        }
    }

    public void RegisterItemBox(ItemBoxController box)
    {
        if (genericHPBarPrefab != null && genericHPBarContainer != null)
        {
            GameObject barObj = Instantiate(genericHPBarPrefab, genericHPBarContainer);
            HPBarController bar = barObj.GetComponent<HPBarController>();

            if (bar != null)
            {
                bar.Init("Item Box", box.maxHp, box.transform);
                _activeBars[box] = bar;
            }
            else
            {
                Debug.LogWarning("GenericHPBarPrefab に HPBarController がアタッチされていません！");
            }
        }
    }

    public void UpdateHP(TankStatus tank, int currentHp, int maxHp)
    {
        if (_activeBars.TryGetValue(tank, out HPBarController bar) && bar != null)
        {
            bar.SetHP(currentHp, maxHp);
            if (currentHp <= 0) { _activeBars.Remove(tank); bar.Hide(); }
        }
    }

    public void UpdateItemBoxHP(ItemBoxController box, int currentHp, int maxHp)
    {
        if (_activeBars.TryGetValue(box, out HPBarController bar) && bar != null)
        {
            bar.SetHP(currentHp, maxHp);
            if (currentHp <= 0) { _activeBars.Remove(box); bar.Hide(); }
        }
    }
}