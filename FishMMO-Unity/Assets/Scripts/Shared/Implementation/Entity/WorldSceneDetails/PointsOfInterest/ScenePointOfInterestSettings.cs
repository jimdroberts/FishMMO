using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sits on a generated scene's "Points of Interest" root: which settings its POIs were made with, and the
	/// asset they were written to. Its editor offers Regenerate POIs.
	/// </summary>
	/// <remarks>Authoring data only; the runtime reads <see cref="ScenePointOfInterest"/> and the details cache.</remarks>
	[DisallowMultipleComponent]
	public class ScenePointOfInterestSettings : MonoBehaviour
	{
		public PointOfInterestSettings Settings;
		public ScenePointsOfInterest Points;
	}
}
