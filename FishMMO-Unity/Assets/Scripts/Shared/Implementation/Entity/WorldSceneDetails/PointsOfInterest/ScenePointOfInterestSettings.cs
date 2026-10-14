using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sits on a generated scene's "Points of Interest" root: which settings its POIs were made with, and the
	/// asset they were written to. Its editor offers Regenerate POIs.
	/// </summary>
	/// <remarks>
	/// Mostly authoring data: the runtime reads <see cref="ScenePointOfInterest"/> and the details cache. The one thing it
	/// does at run time is offer the asset's climbables (<see cref="ScenePointsOfInterest.Climbables"/>) to
	/// <see cref="SceneClimbables"/> while its scene is loaded. This root is in every build, server and client alike (unlike
	/// the client-only <see cref="SceneSiteEmitters"/>), and climbing is movement the server is the authority for.
	/// </remarks>
	[DisallowMultipleComponent]
	public class ScenePointOfInterestSettings : MonoBehaviour
	{
		public PointOfInterestSettings Settings;
		public ScenePointsOfInterest Points;

		private void OnEnable()
		{
			if (Points != null && Points.Climbables != null && Points.Climbables.Count > 0)
			{
				SceneClimbables.Register(gameObject.scene, this, Points.Climbables);
			}
		}

		private void OnDisable()
		{
			SceneClimbables.Unregister(gameObject.scene, this);
		}
	}
}
