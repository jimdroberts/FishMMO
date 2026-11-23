using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// An area where travel spells (Portal to Bind, Recall, Gate) cannot be cast: a prison, a boss arena, a whole scene.
	/// </summary>
	/// <remarks>
	/// <para>Place it on a GameObject with a trigger collider (box, sphere, capsule or convex mesh), or tick
	/// <see cref="CoversWholeScene"/> to forbid travel magic anywhere in its scene. It is read only by the server, when a
	/// travel spell takes effect; it does nothing to movement, teleporters or waypoints.</para>
	/// <para>Zones register by scene handle, so channels of the same scene (stacked instances) never see each other's.</para>
	/// </remarks>
	[DisallowMultipleComponent]
	public class NoTravelZone : MonoBehaviour
	{
		[Tooltip("Forbid travel spells anywhere in this scene. The collider is then not needed.")]
		public bool CoversWholeScene;

		[Tooltip("The area. Left empty, the first collider on this GameObject.")]
		public Collider Area;

		private static readonly Dictionary<SceneHandle, List<NoTravelZone>> zonesByScene = new Dictionary<SceneHandle, List<NoTravelZone>>();

		private SceneHandle registeredScene;
		private bool registered;
		private bool warnedConcave;

		void OnEnable()
		{
			if (Area == null)
			{
				Area = GetComponent<Collider>();
			}
			registeredScene = gameObject.scene.handle;
			if (!zonesByScene.TryGetValue(registeredScene, out List<NoTravelZone> zones))
			{
				zones = new List<NoTravelZone>();
				zonesByScene[registeredScene] = zones;
			}
			zones.Add(this);
			registered = true;
		}

		void OnDisable()
		{
			if (!registered)
			{
				return;
			}
			registered = false;
			if (zonesByScene.TryGetValue(registeredScene, out List<NoTravelZone> zones))
			{
				zones.Remove(this);
				if (zones.Count == 0)
				{
					zonesByScene.Remove(registeredScene);
				}
			}
		}

		/// <summary>Whether this zone covers <paramref name="position"/>.</summary>
		public bool Covers(Vector3 position)
		{
			if (CoversWholeScene)
			{
				return true;
			}
			if (Area == null || !Area.enabled || !Area.bounds.Contains(position))
			{
				return false;
			}
			if (Area is MeshCollider mesh && !mesh.convex)
			{
				// ClosestPoint is undefined for a concave mesh: its bounds are the best answer there is.
				if (!warnedConcave)
				{
					warnedConcave = true;
					Log.Warning("NoTravelZone", $"'{name}' uses a concave mesh collider; its bounding box is used instead. Use a box, sphere, capsule or convex mesh.");
				}
				return true;
			}
			return (Area.ClosestPoint(position) - position).sqrMagnitude < 0.0001f;
		}

		/// <summary>Whether any zone in <paramref name="character"/>'s scene covers where it stands.</summary>
		public static bool Blocks(ICharacter character)
		{
			if (character?.GameObject == null ||
				!zonesByScene.TryGetValue(character.GameObject.scene.handle, out List<NoTravelZone> zones))
			{
				return false;
			}
			Vector3 position = character.Transform.position;
			for (int i = 0; i < zones.Count; ++i)
			{
				if (zones[i] != null && zones[i].Covers(position))
				{
					return true;
				}
			}
			return false;
		}

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			Collider area = Area != null ? Area : GetComponent<Collider>();
			if (area != null && !CoversWholeScene)
			{
				area.DrawGizmo(new Color(0.6f, 0.2f, 1.0f, 0.35f));
			}
		}
#endif
	}
}
