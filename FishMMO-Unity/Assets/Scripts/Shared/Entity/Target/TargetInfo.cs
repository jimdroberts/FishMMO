using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Struct containing information about a target and the hit position.
	/// </summary>
	public struct TargetInfo
	{
		/// <summary>
		/// The transform of the target.
		/// </summary>
		public Transform Target;
		/// <summary>
		/// The position where the target was hit.
		/// </summary>
		public Vector3 HitPosition;

		/// <summary>Initializes a new instance of the TargetInfo struct.</summary>
		/// <param name="target">The target transform.</param>
		/// <param name="hitPosition">The hit position.</param>
		public TargetInfo(Transform target, Vector3 hitPosition)
		{
			Target = target;
			HitPosition = hitPosition;
		}
	}
}