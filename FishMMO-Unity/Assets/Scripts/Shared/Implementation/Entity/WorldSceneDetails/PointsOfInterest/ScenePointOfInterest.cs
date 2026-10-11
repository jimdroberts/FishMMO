using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A generated point of interest in its scene: the transform the site stands at, and the landmark the
	/// world map draws for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Written by the scene generator under the scene's "Points of Interest" root, one per record of the scene's
	/// <see cref="ScenePointsOfInterest"/> asset, and harvested by the world scene details cache into
	/// <see cref="WorldSceneDetails"/> the way waypoints are, so a POI stays on the map whether or not a world map
	/// has been baked. It takes the place of a <see cref="MapPointOfInterest"/> on generated sites, which keeps
	/// working for hand-placed landmarks.
	/// </para>
	/// <para>
	/// Shared and scene-resident in both builds: it carries no visuals and no collider. What a site BUILDS
	/// (spawners, a waypoint, a region, a portal) hangs under it as children.
	/// </para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class ScenePointOfInterest : MonoBehaviour
	{
		/// <summary>The record's id in the scene's POI asset.</summary>
		public int Id;
		public POIType Kind = POIType.Landmark;
		public string PointName;
		[TextArea(1, 3)]
		public string Description;
		/// <summary>The footprint radius in metres.</summary>
		public float Radius = 10f;
		public int DetailTier = 2;
		public bool RequiresDiscovery = true;
		/// <summary>The race's naming key, when the site has one.</summary>
		public string Race;

		/// <summary>The name drawn on the map: the generated name, else the GameObject's.</summary>
		public string ResolvedName => string.IsNullOrWhiteSpace(PointName) ? gameObject.name : PointName;

		/// <summary>Copies a record's fields onto this component and its transform.</summary>
		public void Apply(PointOfInterestRecord record)
		{
			Id = record.Id;
			Kind = record.Kind;
			PointName = record.Name;
			Description = record.Description;
			Radius = record.Radius;
			DetailTier = record.DetailTier;
			RequiresDiscovery = record.RequiresDiscovery;
			Race = record.Race;
			transform.SetPositionAndRotation(record.Position, Quaternion.Euler(0f, record.Yaw, 0f));
		}

		/// <summary>The map entry this POI is drawn as.</summary>
		public MapPointOfInterestDetails ToDetails()
			=> new MapPointOfInterestDetails
			{
				Name = ResolvedName,
				Description = Description,
				Position = transform.position,
				Type = PointOfInterestKinds.MarkerFor(Kind),
				DetailTier = Mathf.Max(0, DetailTier),
				RequiresDiscovery = RequiresDiscovery,
				ShowOnMinimap = true,
			};

#if UNITY_EDITOR
		/// <summary>How high the marker line rises above the site, metres, with the label at its top (Jim, 2026-10-10).</summary>
		private const float MarkerHeight = 20f;

		private static GUIStyle labelStyle;

		/// <summary>
		/// A line into the sky in the site's group colour and its name at the top, so every generated site can be found in
		/// the Scene view over trees and hills. Editor only.
		/// </summary>
		private void OnDrawGizmos()
		{
			PointOfInterestKindInfo info = PointOfInterestKinds.Info(Kind);
			Color colour = PointOfInterestKinds.GroupColour(info.Group);
			Vector3 foot = transform.position;
			Vector3 top = foot + Vector3.up * MarkerHeight;
			Gizmos.color = colour;
			Gizmos.DrawLine(foot, top);
			Gizmos.DrawWireSphere(foot, 1.5f);
			Gizmos.DrawSphere(top, 0.6f);

			if (labelStyle == null)
			{
				labelStyle = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
				{
					alignment = TextAnchor.LowerCenter,
					fontSize = 11,
					padding = new RectOffset(4, 4, 2, 2),
				};
				var backing = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
				backing.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
				backing.Apply();
				labelStyle.normal.background = backing;
			}
			labelStyle.normal.textColor = colour;
			UnityEditor.Handles.Label(top + Vector3.up * 0.8f, $"{ResolvedName}\n{info.DisplayName}", labelStyle);
		}

		private void OnDrawGizmosSelected()
		{
			Gizmos.color = new Color(0.36f, 0.78f, 0.95f, 0.35f);
			const int segments = 48;
			Vector3 previous = transform.position + new Vector3(Radius, 0f, 0f);
			for (int i = 1; i <= segments; i++)
			{
				float a = i * Mathf.PI * 2f / segments;
				Vector3 next = transform.position + new Vector3(Mathf.Cos(a) * Radius, 0f, Mathf.Sin(a) * Radius);
				Gizmos.DrawLine(previous, next);
				previous = next;
			}
		}
#endif
	}
}
