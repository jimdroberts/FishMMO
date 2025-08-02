using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract MonoBehaviour for defining scene boundaries. Provides methods to get offset and size of the boundary.
	/// </summary>
	public abstract class IBoundary : MonoBehaviour
	{
		/// <summary>
		/// Gets the offset of the boundary from the object's position.
		/// </summary>
		/// <returns>The offset vector for the boundary.</returns>
		public abstract Vector3 GetBoundaryOffset();

		/// <summary>
		/// Gets the size of the boundary.
		/// </summary>
		/// <returns>The size vector of the boundary.</returns>
		public abstract Vector3 GetBoundarySize();
	}
}