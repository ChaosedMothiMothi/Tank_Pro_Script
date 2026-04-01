using UnityEngine;

public class RobotBombSensor : MonoBehaviour
{
    private RobotBombController _controller;

    public void Init(RobotBombController controller)
    {
        _controller = controller;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_controller != null)
        {
            _controller.OnSensorDetect(other);
        }
    }
}