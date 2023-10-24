using UnityEngine;


public abstract class IBoundary : MonoBehaviour
{
	public abstract Vector3 GetBoundaryOffset();

	public abstract Vector3 GetBoundarySize();
}
