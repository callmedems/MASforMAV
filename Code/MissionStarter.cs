using UnityEngine;

public class MissionStarter : MonoBehaviour
{
    public DroneController drone;
    public Vector2 targetXZ = new Vector2(54f, -36f); // <-- AJUSTA ESTO (x,z)

    void Start()
    {
        if (drone != null)
            drone.GoToXZ(targetXZ.x, targetXZ.y);
    }
}
