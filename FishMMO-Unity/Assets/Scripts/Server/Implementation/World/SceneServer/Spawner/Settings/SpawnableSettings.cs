using UnityEngine;
using FishNet.Object;
using System;
using UnityEngine.Scripting.APIUpdating;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Spawner
{
	/// <summary>
	/// Base serializable settings for configuring a spawnable object. Supports polymorphic subclassing
	/// via <see cref="SerializeReference"/> for type-specific data injection (e.g., items, NPCs).
	/// Use the <see cref="SubclassSelectorAttribute"/> on the containing field/list for Inspector support.
	/// </summary>
	/// <remarks>
	/// Stored by <see cref="SerializeReference"/> on the scene's <see cref="ObjectSpawner"/>
	/// authoring components and in the baked <see cref="SceneSpawnTable"/>. Scenes authored before
	/// the spawner moved to the server assembly name the old assembly; <see cref="MovedFromAttribute"/>
	/// is what lets Unity resolve those records.
	/// </remarks>
	[Serializable]
	[MovedFrom(false, sourceNamespace: "FishMMO.Shared", sourceAssembly: "FishMMO.Shared", sourceClassName: "SpawnableSettings")]
	public class SpawnableSettings
	{
		/// <summary>
		/// The network object prefab to be spawned.
		/// </summary>
		public NetworkObject NetworkObject;

		/// <summary>
		/// The minimum respawn time (in seconds) for this object.
		/// </summary>
		/// <remarks>
		/// Subclasses whose prefab carries its own respawn cadence treat this as an OVERRIDE that
		/// is only consulted when set — see <see cref="NPCSpawnableSettings"/>. For everything
		/// else it is simply the authored value.
		/// </remarks>
		[Tooltip("Shortest respawn delay in seconds. For NPCs, leave at 0 to use the prefab's own value.")]
		[Min(0f)]
		public float MinimumRespawnTime;

		/// <summary>
		/// The maximum respawn time (in seconds) for this object.
		/// </summary>
		/// <remarks>
		/// See <see cref="MinimumRespawnTime"/> for the override semantics.
		/// </remarks>
		[Tooltip("Longest respawn delay in seconds. For NPCs, leave at 0 to use the prefab's own value.")]
		[Min(0f)]
		public float MaximumRespawnTime;

		/// <summary>
		/// The chance (0 to 1) that this object will be selected for spawning. Default is 0.5 (50%).
		/// </summary>
		[Range(0f, 1f)]
		public float SpawnChance = 0.5f;

		/// <summary>
		/// The vertical offset used when placing the object in the world, calculated from its collider.
		/// </summary>
		[ShowReadonly]
		public float YOffset;

		/// <summary>
		/// Resolves the respawn delay range this spawnable should use.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Exists so the spawner asks the settings for a range rather than reading the two fields
		/// directly. That indirection is the whole point: a subclass whose prefab carries its own
		/// respawn cadence can answer with the prefab's values when the spawner has not overridden
		/// them, and the spawner does not need to know which subclass it is holding.
		/// </para>
		/// <para>
		/// The base implementation has no prefab defaults to fall back on, so it returns the
		/// authored fields unchanged.
		/// </para>
		/// </remarks>
		/// <param name="minimum">Receives the shortest respawn delay in seconds.</param>
		/// <param name="maximum">Receives the longest respawn delay in seconds.</param>
		public virtual void ResolveRespawnTimeRange(out float minimum, out float maximum)
		{
			minimum = MinimumRespawnTime;
			maximum = MaximumRespawnTime;

			// The RNG collapses an inverted range to its minimum, which would silently pin the
			// delay to a constant. Order it here so every caller gets a usable range.
			if (maximum < minimum)
			{
				maximum = minimum;
			}
		}

		/// <summary>
		/// Validates the spawnable settings, ensuring the network object is spawnable and calculates YOffset from its collider.
		/// </summary>
		public virtual void OnValidate()
		{
			if (MinimumRespawnTime < 0f)
			{
				MinimumRespawnTime = 0f;
			}
			/* Raise the maximum to meet the minimum rather than leaving the pair incoherent.
			 *
			 * This is what makes "is the override set?" answerable from the maximum alone, which
			 * is how NPCSpawnableSettings decides whether to defer to the prefab. An author who
			 * fills in a minimum of 10 and leaves the maximum at 0 has plainly asked for an
			 * override; without this the pair would still read as unset and their value would be
			 * silently discarded in favour of the prefab's. It also orders a range someone
			 * inverted, which the RNG would otherwise collapse to a constant.
			 */
			if (MaximumRespawnTime < MinimumRespawnTime)
			{
				MaximumRespawnTime = MinimumRespawnTime;
			}

			if (NetworkObject == null)
			{
				return;
			}

			// Ensure the network object is marked as spawnable.
			if (!NetworkObject.GetIsSpawnable())
			{
				Log.Error("SpawnableSettings", $"{NetworkObject.name} is not spawnable. Mark it as spawnable and re-assign the object.");
				NetworkObject = null;
				return;
			}

			// Get the collider and calculate YOffset for proper placement.
			Collider collider = NetworkObject.GetComponent<Collider>();
			if (collider != null)
			{
				YOffset = PivotAboveBottom(collider);
			}
		}

		/// <summary>
		/// How far the prefab's pivot stands above its collider's lowest point, metres: what lifts it so its collider just
		/// rests on the ground the spawner found. 0 for a prefab whose pivot is at its base (a gathering node, a tree, a
		/// character on its feet), half its height for one pivoted at its middle.
		/// </summary>
		/// <remarks>
		/// It was the collider's full height (a sphere's radius), right only for a pivot that far below the collider: every
		/// base-pivot prefab on a random-position spawner hovered its own height over the ground — a herb 0.8 m up, an ore
		/// vein 1.2 m, an ancient oak 27 m (2026-10-12: "Herb Grove has no herbs?").
		/// </remarks>
		public static float PivotAboveBottom(Collider collider)
		{
			float scale = Mathf.Abs(collider.transform.lossyScale.y);
			float bottom;
			switch (collider)
			{
				case BoxCollider box:
					bottom = box.center.y - box.size.y * 0.5f;
					break;
				case CapsuleCollider capsule:
					bottom = capsule.center.y - (capsule.direction == 1 ? capsule.height * 0.5f : capsule.radius);
					break;
				case SphereCollider sphere:
					bottom = sphere.center.y - sphere.radius;
					break;
				case MeshCollider mesh when mesh.sharedMesh != null:
					bottom = mesh.sharedMesh.bounds.min.y;
					break;
				case CharacterController controller:
					bottom = controller.center.y - controller.height * 0.5f;
					break;
				default:
					collider.TryGetDimensions(out float height, out _);
					bottom = -0.5f * height;
					break;
			}
			return Mathf.Max(0f, -bottom * scale);
		}

		/// <summary>
		/// Called after the network object has been instantiated but before it is spawned on the network.
		/// Override in subclasses to inject type-specific data into the spawned object.
		/// </summary>
		/// <param name="nob">The instantiated network object to configure.</param>
		/// <param name="spawner">The spawner that created this object.</param>
		public virtual void OnSpawned(NetworkObject nob, SpawnerRuntime spawner)
		{
		}
	}
}