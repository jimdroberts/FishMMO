using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class IBoundary : MonoBehaviour
	{
		public abstract Vector3 GetBoundaryOffset();

		public abstract Vector3 GetBoundarySize();
	}
}