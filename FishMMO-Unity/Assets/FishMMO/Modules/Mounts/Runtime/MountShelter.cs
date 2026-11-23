using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Whether a place is indoors or underground, where nobody rides: a roof, a ceiling or a cave's rock over the rider's head, or ground that reads as underground.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Physics, not the cut's rooms.</b> The rooms the cut bakes (SceneRoom) reach only the client (their object is
	/// client-only), and the server is the one that dismounts, so this asks the scene's colliders, which both builds keep: a
	/// ray straight up from the head against the solid layers, characters and triggers (an open door) ignored, and the ground
	/// under the feet through <see cref="GroundQuery"/> (a cave's rock floor, or the terrain's surface well over it).
	/// </para>
	/// <para>
	/// A market stall's awning or a bridge overhead count too: anything solid within <see cref="RoofReach"/> of the head.
	/// </para>
	/// </remarks>
	public static class MountShelter
	{
		/// <summary>How far over the rider's head a roof still shelters it, metres.</summary>
		public const float RoofReach = 8f;

		/// <summary>Where the head is over the feet, metres (a rider sits higher than a walker stands).</summary>
		public const float HeadHeight = 2.2f;

		private static readonly RaycastHit[] hits = new RaycastHit[8];
		private static int solidMask = -1;

		/// <summary>The layers a roof or the ground can be on: Default and Ground.</summary>
		private static int SolidMask
		{
			get
			{
				if (solidMask == -1)
				{
					int mask = LayerMask.GetMask("Default", "Ground");
					solidMask = mask != 0 ? mask : Physics.DefaultRaycastLayers;
				}
				return solidMask;
			}
		}

		/// <summary>Whether <paramref name="character"/> stands indoors or underground now.</summary>
		public static bool IsSheltered(ICharacter character)
		{
			if (character == null || character.Transform == null)
			{
				return false;
			}
			return IsSheltered(character.Transform.gameObject.scene, character.Transform.position);
		}

		/// <summary>Whether the place whose ground is at <paramref name="feet"/> is indoors or underground.</summary>
		public static bool IsSheltered(Scene scene, Vector3 feet)
		{
			if (!scene.IsValid())
			{
				return false;
			}
			PhysicsScene physics = scene.GetPhysicsScene();
			if (!physics.IsValid())
			{
				return false;
			}
			// Something solid overhead that is not a character: a roof, a ceiling, the rock of a cave.
			Vector3 head = feet + Vector3.up * HeadHeight;
			if (FirstSolid(physics, head, Vector3.up, RoofReach, out _))
			{
				return true;
			}
			// The ground underfoot is a cave's (its rock, or the terrain's surface well over it).
			if (FirstSolid(physics, feet + Vector3.up * 0.5f, Vector3.down, 3f, out RaycastHit ground) &&
				GroundQuery.Read(scene, in ground).Underground)
			{
				return true;
			}
			return false;
		}

		/// <summary>The nearest solid, non-character collider along a ray.</summary>
		private static bool FirstSolid(PhysicsScene physics, Vector3 from, Vector3 direction, float reach, out RaycastHit nearest)
		{
			nearest = default;
			int count = physics.Raycast(from, direction, hits, reach, SolidMask, QueryTriggerInteraction.Ignore);
			float best = float.MaxValue;
			bool found = false;
			for (int i = 0; i < count; i++)
			{
				RaycastHit hit = hits[i];
				if (hit.collider == null || hit.distance >= best || hit.collider.GetComponentInParent<ICharacter>() != null)
				{
					continue;
				}
				best = hit.distance;
				nearest = hit;
				found = true;
			}
			return found;
		}
	}
}
