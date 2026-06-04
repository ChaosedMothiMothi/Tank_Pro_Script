using UnityEngine;

/// <summary>
///  ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "NewShellData", menuName = "TankGame/ShellData")]
public class ShellData : ScriptableObject
{
    [Tooltip("�e�̖���")]
    public string shellName = "Normal Shell";

    [Tooltip("�e�̑��x")]
    public float speed = 10f;

    [Tooltip("�e�̃_���[�W")]
    public int damage = 10;

    [Tooltip("���e��")]
    public int maxBounces = 1;

    [Tooltip("�e�̎c�����ԁ@����{������܂���")]
    public float lifeTime = 360f;

    [Header("�����e�̐ݒ�")]
    [Tooltip("�����e��")]
    public bool isExplosive;

    [Tooltip("�����_���[�W")]
    public int explosionDamage = 20;

    [Tooltip("�������a")]
    public float explosionRadius = 3f;

    [Tooltip("��������̎c������")]
    public float explosionDuration = 0.2f;

    [Tooltip("�����G�t�F�N�g�̃v���n�u")]
    public GameObject explosionPrefab;

    [Tooltip("�����G�t�F�N�g�̎c������")]
    public float explosionLifetime = 1f;

    [Header("�Ή����ːݒ�i�p�[�e�B�^���N��p�e�ݒ�j")]
    [Tooltip("�Ή����˒e��")]
    public bool isFlamethrower = false;

    [Tooltip("�������ђʂ��邩")]
    public bool ignoreExplosionsAndShells = false;

    [Tooltip("�g�傷�邩")]
    public bool scaleOverTime = false;

    [Tooltip("�ǂꂾ���傫���Ȃ邩")]
    public float scaleMultiplier = 2.0f;

    [Tooltip("�ǂɓ���������̏������Ȃ�����")]
    public bool muteDestroySound = false;
}
