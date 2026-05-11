using UnityEngine;
using UnityEngine.InputSystem;

public class GamepadTest : MonoBehaviour
{
    private void Update()
    {
        // 接続されているゲームパッドがない場合は何もしない
        if (Gamepad.current == null) return;

        // 右スティックの入力を直接読み取る
        Vector2 rightStick = Gamepad.current.rightStick.ReadValue();

        // 少しでも入力があればログを出す
        if (rightStick.sqrMagnitude > 0.01f)
        {
            Debug.Log($"<color=cyan>[Gamepad Test]</color> 右スティック入力: {rightStick}");
        }
    }
}