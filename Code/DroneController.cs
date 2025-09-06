using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;        // NavMesh, NavMeshPath, NavMeshHit
using Unity.AI.Navigation;   // NavMeshSurface, NavMeshModifier, NavMeshObstacle

public class DroneController : MonoBehaviour
{
    [Header("Refs")]
    public Transform droneVisual;      // malla del dron (hijo)
    public NavMeshSurface surface;     // NavMeshSurface del Ground

    [Header("Vuelo")]
    public float cruiseAltitude = 12f; // altura fija de crucero (Y)
    public float speed = 8f;           // m/s
    public float turnSpeed = 5f;       // suavizado de rotación

    [Header("Aterrizaje (detección)")]
    [Tooltip("Radio del volumen para comprobar espacio libre al aterrizar")]
    public float landingCheckRadius = 1.2f;
    [Tooltip("Altura del volumen (capsule) para comprobar espacio libre")]
    public float landingClearHeight = 2.0f;
    [Tooltip("Capas que bloquean el aterrizaje (Obstacles, Water, etc.)")]
    public LayerMask landingBlockMask;
    [Tooltip("Búsqueda de sitio alterno si el objetivo está bloqueado")]
    public float safeSearchRadius = 6f;
    public int safeSamples = 24;

    [Header("Debug")]
    public bool drawPath = true;

    // estado interno
    private readonly List<Vector3> _pathPoints = new();
    private int _pathIndex = 0;
    private bool _enRoute = false;
    private bool _landing = false;
    public bool logLandingDebug = true;
    // destino de misión (XZ del mundo, Y la tomamos del suelo)
    private Vector3 _missionXZ;

    void Awake()
{
    if (surface != null) surface.BuildNavMesh();
    if (droneVisual == null) droneVisual = transform;

    if (landingBlockMask == 0)
        landingBlockMask = LayerMask.GetMask("Obstacles", "Water");

    Debug.Log($"[Drone] landingBlockMask={landingBlockMask} (debe ser >0).");
}


    
    public void GoToXZ(float x, float z) //aqui planea la ruta como esquinas y pone enroute como true para que update empiece a seguir esa ruta
    {
        _missionXZ = new Vector3(x, 0f, z);

        // Proyecta el destino a la NavMesh (en el suelo)
        if (NavMesh.SamplePosition(_missionXZ, out var hit, 5f, NavMesh.AllAreas))
        {
            // Punto de inicio proyectado a NavMesh (XZ actual del dron)
            var startXZ = new Vector3(transform.position.x, 0f, transform.position.z);
            if (!NavMesh.SamplePosition(startXZ, out var startHit, 5f, NavMesh.AllAreas))
            {
                Debug.LogWarning("No se pudo proyectar el punto de inicio en la NavMesh.");
                return;
            }

            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(startHit.position, hit.position, NavMesh.AllAreas, path) &&
                path.corners != null && path.corners.Length > 0)
            {
                _pathPoints.Clear();
                foreach (var p in path.corners)
                    _pathPoints.Add(new Vector3(p.x, cruiseAltitude, p.z)); // elevamos la ruta a la altura de vuelo

                _pathIndex = 0;
                _enRoute = true;
                _landing = false;
            }
            else
            {
                Debug.LogWarning("No se pudo calcular un camino en la NavMesh al destino.");
            }
        }
        else
        {
            Debug.LogWarning("Destino fuera de la NavMesh o no alcanzable.");
        }
    }

    void Update()
    {
        if (_enRoute && _pathIndex < _pathPoints.Count)
        {
            FlyAlongPath();
            return;
        }

        // Llegamos al final de la ruta -> intentar aterrizar cerca del destino de misión
        if (_enRoute && _pathIndex >= _pathPoints.Count && !_landing)
        {
            TryLandNearMissionXZ();
        }
    }

   void FlyAlongPath()
{
    Vector3 target = _pathPoints[_pathIndex];
    Vector3 dir = (target - transform.position);

    // ⬅️ evita rotación con vector cero
    if (dir.sqrMagnitude < 1e-6f)
    {
        _pathIndex++;
        return;
    }

    float dist = dir.magnitude;
    if (dist < 0.3f) { _pathIndex++; return; }

    Vector3 step = dir.normalized * speed * Time.deltaTime;
    transform.position += step;

    if (step.sqrMagnitude > 1e-6f)
    {
        var flat = new Vector3(step.x, 0, step.z);
        if (flat.sqrMagnitude > 1e-6f)
        {
            Quaternion look = Quaternion.LookRotation(flat, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
    }
}


    void TryLandNearMissionXZ()
    {
        _landing = true;

        // Busca primero en el punto exacto, luego alrededor
        if (FindSafeLanding(_missionXZ, out var safePoint))
        {
            StartCoroutine(LandCoroutine(safePoint));
        }
        else
        {
            Debug.LogWarning("No se encontró zona segura de aterrizaje cerca del destino.");
            _landing = false; // permitir reintentos si quieren
        }
    }

    System.Collections.IEnumerator LandCoroutine(Vector3 groundPoint)
    {
        Vector3 target = new Vector3(groundPoint.x, groundPoint.y + 0.2f, groundPoint.z);

        // Desciende vertical
        while (Vector3.Distance(transform.position, target) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, (speed * 0.6f) * Time.deltaTime);
            yield return null;
        }

        _enRoute = false;
        _landing = false;
        Debug.Log("Aterrizaje completado.");
    }

    /// <summary>
    /// Intenta encontrar un punto libre de obstáculos cercano a centerXZ.
    /// </summary>
    bool FindSafeLanding(Vector3 centerXZ, out Vector3 safeGroundPoint)
    {
        // 1) punto exacto
        if (IsClearForLanding(centerXZ, out safeGroundPoint))
            return true;

        // 2) muestreo alrededor (círculo/espiral)
        for (int i = 0; i < safeSamples; i++)
        {
            float t = (float)i / Mathf.Max(1, safeSamples - 1);
            float angle = t * Mathf.PI * 2f;
            float radius = Mathf.Lerp(0.6f, safeSearchRadius, t);

            Vector3 probe = centerXZ + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;

            if (IsClearForLanding(probe, out safeGroundPoint))
                return true;
        }

        safeGroundPoint = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Devuelve true si hay espacio libre para posar el dron en xz (proyectado al suelo).
    /// Comprueba: punto en NavMesh, suelo válido y volumen libre contra landingBlockMask.
    /// </summary>
    bool IsClearForLanding(Vector3 xz, out Vector3 groundPoint)
{
    if (!NavMesh.SamplePosition(xz, out var navHit, 1.5f, NavMesh.AllAreas))
    {
        if (logLandingDebug) Debug.Log("[Landing] No NavMesh at " + xz);
        groundPoint = Vector3.zero; return false;
    }

    // raycast vertical
    if (!Physics.Raycast(navHit.position + Vector3.up * 30f,
                         Vector3.down, out var downHit, 60f, ~0,
                         QueryTriggerInteraction.Ignore))
    {
        if (logLandingDebug) Debug.Log("[Landing] No ground ray hit.");
        groundPoint = Vector3.zero; return false;
    }

    groundPoint = downHit.point;

    int hitLayerMask = 1 << downHit.collider.gameObject.layer;
    if ((hitLayerMask & landingBlockMask) != 0)
    {
        if (logLandingDebug) Debug.Log($"[Landing] Ground is blocked by {downHit.collider.name} (layer {LayerMask.LayerToName(downHit.collider.gameObject.layer)}).");
        return false;
    }

    Vector3 p1 = groundPoint + Vector3.up * 0.2f;
    Vector3 p2 = groundPoint + Vector3.up * landingClearHeight;

    var hits = Physics.OverlapCapsule(p1, p2, landingCheckRadius, landingBlockMask, QueryTriggerInteraction.Collide);
    if (hits != null && hits.Length > 0)
    {
        Debug.Log($"[Drone] 🚨 ¡COLISIÓN detectada! El dron intentó aterrizar pero había {hits.Length} objetos bloqueando.");

        foreach (var h in hits)
        {
            Debug.Log($"[Drone] Objeto bloqueante: {h.name} en layer {LayerMask.LayerToName(h.gameObject.layer)}");
        }

        return false;
    }

    if (logLandingDebug) Debug.Log("[Landing] CLEAR at " + groundPoint);
    return true;
}
    void OnDrawGizmos()
    {
        if (!drawPath || _pathPoints == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < _pathPoints.Count - 1; i++)
        {
            Gizmos.DrawLine(_pathPoints[i], _pathPoints[i + 1]);
            Gizmos.DrawSphere(_pathPoints[i], 0.15f);
        }
        if (_pathPoints.Count > 0)
            Gizmos.DrawSphere(_pathPoints[^1], 0.2f);
    }
}
