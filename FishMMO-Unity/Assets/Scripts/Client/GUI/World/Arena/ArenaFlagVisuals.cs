using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws the flags of a Capture the Flag match: a marker over a carrier's head, and a marker
	/// where a dropped flag lies.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Driven entirely by <see cref="ArenaMatchStateBroadcast.Objectives"/>; the client keeps no
	/// flag state of its own. Each state the HUD receives is reconciled against what is drawn:
	/// markers for flags that went home are destroyed, carriers who changed get their marker
	/// moved, and a dropped flag's marker is placed where the server said it fell.
	/// </para>
	/// <para>
	/// The template may supply prefabs for both markers. Without them a tinted primitive is used,
	/// so an arena works before any art exists. Markers are tinted with the flag's team colour,
	/// from the template.
	/// </para>
	/// </remarks>
	public static class ArenaFlagVisuals
	{
		private sealed class Marker
		{
			public GameObject Object;
			public long ObjectiveID;
			public long CarrierID;
			public bool Dropped;
		}

		/// <summary>Height above a carrier's origin at which the marker floats.</summary>
		private const float CarrierMarkerHeight = 2.4f;

		private static readonly Dictionary<long, Marker> markers = new Dictionary<long, Marker>();

		/// <summary>Brings the drawn flags into line with a match state.</summary>
		public static void Apply(ArenaMatchStateBroadcast state, ArenaTemplate template)
		{
#if UNITY_SERVER
			/* Arena flag markers are a client view. BaseCharacter.ClientCharacters does not exist
			 * in a server build, and a server draws no markers. */
			return;
#else
			if (state.Objectives == null || state.Phase == ArenaMatchPhase.Ended || state.Phase == ArenaMatchPhase.Cancelled)
			{
				Clear();
				return;
			}

			var seen = new HashSet<long>();
			foreach (ArenaObjectiveEntry objective in state.Objectives)
			{
				if (objective.Kind != ArenaObjectiveKind.FlagStand)
				{
					continue;
				}

				var flag = (ArenaFlagState)Mathf.Clamp(objective.Progress, 0, 2);
				if (flag == ArenaFlagState.Home)
				{
					continue;
				}

				seen.Add(objective.ObjectiveID);
				Color color = template != null ? template.GetTeamColor(objective.Team) : ArenaTeamColors.Default(objective.Team);

				if (flag == ArenaFlagState.Carried)
				{
					if (!BaseCharacter.ClientCharacters.TryGetValue(objective.Holder, out ICharacter carrier) || carrier?.Transform == null)
					{
						// Carrier is culled from view; nothing to hang the marker on. Drop any stale one.
						Remove(objective.ObjectiveID);
						continue;
					}
					Marker marker = Ensure(objective.ObjectiveID, dropped: false, template != null ? template.FlagCarrierVisualPrefab : null, color);
					if (marker.CarrierID != objective.Holder || marker.Object.transform.parent != carrier.Transform)
					{
						marker.CarrierID = objective.Holder;
						marker.Object.transform.SetParent(carrier.Transform, false);
						marker.Object.transform.localPosition = new Vector3(0f, CarrierMarkerHeight, 0f);
						marker.Object.transform.localRotation = Quaternion.identity;
					}
				}
				else
				{
					Marker marker = Ensure(objective.ObjectiveID, dropped: true, template != null ? template.DroppedFlagVisualPrefab : null, color);
					marker.CarrierID = 0;
					if (marker.Object.transform.parent != null)
					{
						marker.Object.transform.SetParent(null, true);
					}
					marker.Object.transform.position = objective.Position + Vector3.up * 0.6f;
				}
			}

			var stale = new List<long>();
			foreach (long id in markers.Keys)
			{
				if (!seen.Contains(id))
				{
					stale.Add(id);
				}
			}
			foreach (long id in stale)
			{
				Remove(id);
			}
		#endif
		}

		/// <summary>Destroys every marker.</summary>
		public static void Clear()
		{
			foreach (Marker marker in markers.Values)
			{
				if (marker.Object != null)
				{
					Object.Destroy(marker.Object);
				}
			}
			markers.Clear();
		}

		private static Marker Ensure(long objectiveID, bool dropped, GameObject prefab, Color color)
		{
			if (markers.TryGetValue(objectiveID, out Marker marker) && marker.Object != null && marker.Dropped == dropped)
			{
				return marker;
			}
			Remove(objectiveID);

			GameObject obj;
			if (prefab != null)
			{
				obj = Object.Instantiate(prefab);
			}
			else
			{
				obj = GameObject.CreatePrimitive(dropped ? PrimitiveType.Cylinder : PrimitiveType.Cube);
				obj.transform.localScale = dropped ? new Vector3(0.6f, 0.6f, 0.6f) : new Vector3(0.35f, 0.5f, 0.08f);
				Collider collider = obj.GetComponent<Collider>();
				if (collider != null)
				{
					Object.Destroy(collider);
				}
			}
			obj.name = dropped ? $"ArenaDroppedFlag_{objectiveID}" : $"ArenaFlagCarrier_{objectiveID}";

			foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
			{
				// A per-renderer instance so the shared material is not tinted for everyone.
				Material material = renderer.material;
				if (material != null)
				{
					if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
					if (material.HasProperty("_Color")) material.color = color;
				}
			}

			marker = new Marker { Object = obj, ObjectiveID = objectiveID, Dropped = dropped };
			markers[objectiveID] = marker;
			return marker;
		}

		private static void Remove(long objectiveID)
		{
			if (markers.TryGetValue(objectiveID, out Marker marker))
			{
				if (marker.Object != null)
				{
					Object.Destroy(marker.Object);
				}
				markers.Remove(objectiveID);
			}
		}
	}
}
