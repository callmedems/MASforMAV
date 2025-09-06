using UnityEngine;

public class PrintBounds : MonoBehaviour
{
    void Start()
    {
        var r = GetComponent<Renderer>();
        if (r != null)
        {
            Bounds b = r.bounds;
            Debug.Log($"Ground bounds:\nMin {b.min}  Max {b.max}  Size {b.size}");
        }
        var t = GetComponent<Terrain>();
        if (t != null)
        {
            var size = t.terrainData.size;
            var pos = t.GetPosition();
            Debug.Log($"Terrain pos {pos}  size {size}  max {(pos + size)}");
        }
    }
}
