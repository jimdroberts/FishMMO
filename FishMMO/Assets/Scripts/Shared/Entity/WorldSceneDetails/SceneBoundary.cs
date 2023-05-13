using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SceneBoundary : MonoBehaviour
{
    [Header("Scene Boundaries are *inclusive*, if a player is not within one, they'll respawn!")]
    public Vector3 BoundarySize;

    public void OnDrawGizmos()
    {
        Gizmos.color = Color.grey;
        Gizmos.DrawWireCube(transform.position, BoundarySize);
    }

    public void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawCube(transform.position, BoundarySize);
    }
}
