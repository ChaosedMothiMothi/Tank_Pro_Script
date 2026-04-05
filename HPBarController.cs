using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class HPBarController : MonoBehaviour
{
    [Header("UI Elements")]
    public TMPro.TextMeshProUGUI nameText;
    [Tooltip("即座に減る手前のバー（赤など）")]
    public Image frontHpBar;
    [Tooltip("ゆっくり減る奥のバー（黒や黄色など）")]
    public Image backHpBar;

    private float _targetFill;
    private float _shakeTimer;

    private RectTransform _rectTransform;
    private Vector3 _basePos;
    private Transform _followTarget;

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Init(string targetName, int maxHp, Transform targetTransform = null)
    {
        if (nameText != null) nameText.text = targetName;
        frontHpBar.fillAmount = 1f;
        backHpBar.fillAmount = 1f;
        _targetFill = 1f;
        _followTarget = targetTransform;
        gameObject.SetActive(true);

        if (_followTarget == null)
        {
            _basePos = _rectTransform.position;
        }
    }

    public void SetHP(int currentHp, int maxHp)
    {
        _targetFill = Mathf.Clamp01((float)currentHp / maxHp);

        // ★修正: 手前のバー（Front）はアニメーションさせず、「即座に」減らす
        frontHpBar.fillAmount = _targetFill;

        _shakeTimer = 0.3f; // 0.3秒間シェイク
    }

    private void Update()
    {
        // ★修正: 奥のバー（Back）だけを、手前のバーに向かって「ゆっくり」減らす
        if (backHpBar.fillAmount > _targetFill)
        {
            // Lerpで滑らかに追いかける
            backHpBar.fillAmount = Mathf.Lerp(backHpBar.fillAmount, _targetFill, Time.deltaTime * 3f);
        }

        // 追従処理（ターゲットがいれば画面上の位置を計算）
        if (_followTarget != null)
        {
            Vector3 worldPos = _followTarget.position + Vector3.down * 5.5f;
            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);

            if (screenPos.z < 0) screenPos.y = -9999f;
            _basePos = screenPos;
        }

        // シェイク演出
        if (_shakeTimer > 0)
        {
            _shakeTimer -= Time.deltaTime;
            _rectTransform.position = _basePos + (Vector3)(Random.insideUnitCircle * 8f);
        }
        else
        {
            _rectTransform.position = _basePos;
        }
    }

    public void Hide()
    {
        StartCoroutine(HideRoutine());
    }

    private IEnumerator HideRoutine()
    {
        _targetFill = 0f;
        frontHpBar.fillAmount = 0f; // 死亡時も即座に0にする
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }
}