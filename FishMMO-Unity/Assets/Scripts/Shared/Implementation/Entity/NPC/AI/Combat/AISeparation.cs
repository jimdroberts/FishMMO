using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Keeps NPCs from standing inside one another without Unity's crowd avoidance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Unity's NavMesh crowd is one global simulation. A scene server stacks several instances
	/// of the same world scene at the same coordinates, so with obstacle avoidance on, an NPC in
	/// one instance steered around NPCs in every other — invisible shoves, tripped stuck timers,
	/// spawner-mates in two instances pushing each other off their paths. Avoidance is therefore
	/// off on every NPC agent (<c>AIController.InitializeOnce</c>) and this takes its place.
	/// </para>
	/// <para>
	/// It is scene-scoped by construction: neighbours come from the NPC's own
	/// <see cref="PhysicsScene"/>, which the scene server gives every stacked instance separately
	/// (<c>LocalPhysicsMode.Physics3D</c>). Attackers around one target are spaced by
	/// <see cref="AICombatSlots"/>; this only stops bodies overlapping in the wander, idle and
	/// approach cases the ring does not cover.
	/// </para>
	/// </remarks>
	public static class AISeparation
	{
		/// <summary>Minimum neighbour distance below which a fixed sideways push is used.</summary>
		private const float COINCIDENT_SQR = 1e-6f;

		/// <summary>
		/// The velocity that moves <paramref name="self"/> out of its neighbours' bodies.
		/// </summary>
		/// <param name="self">This NPC's position.</param>
		/// <param name="neighbours">Positions of nearby NPC bodies.</param>
		/// <param name="radius">Distance at which a neighbour starts to push, in metres.</param>
		/// <param name="maxSpeed">Push speed when fully overlapped, in metres per second.</param>
		/// <returns>A horizontal velocity, zero when nothing is inside the radius.</returns>
		public static Vector3 Resolve(Vector3 self, IReadOnlyList<Vector3> neighbours, float radius, float maxSpeed)
		{
			if (neighbours == null || neighbours.Count == 0 || radius <= 0f || maxSpeed <= 0f)
			{
				return Vector3.zero;
			}

			Vector3 push = Vector3.zero;
			for (int i = 0; i < neighbours.Count; ++i)
			{
				Vector3 away = self - neighbours[i];
				away.y = 0f;

				float sqrDistance = away.sqrMagnitude;
				if (sqrDistance >= radius * radius)
				{
					continue;
				}

				if (sqrDistance < COINCIDENT_SQR)
				{
					// Exactly on top of each other: any direction is out. Pick a stable one.
					away = Vector3.right;
					sqrDistance = 0f;
				}

				float distance = Mathf.Sqrt(sqrDistance);
				float weight = 1f - distance / radius;
				push += away.normalized * weight;
			}

			float strength = push.magnitude;
			if (strength <= 0f)
			{
				return Vector3.zero;
			}

			return push / strength * Mathf.Min(maxSpeed, strength * maxSpeed);
		}
	}
}
