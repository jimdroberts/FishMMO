using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
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
	/// <see cref="PhysicsScene"/>'s <see cref="AIBodyGrid"/>, which is separate for every stacked
	/// instance. Attackers around one target are spaced by <see cref="AICombatSlots"/>; this only
	/// stops bodies overlapping in the wander, idle and approach cases the ring does not cover.
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
		/// <param name="selfKey">This NPC's identity key; breaks the tie when two bodies coincide.</param>
		/// <param name="neighbours">Positions of nearby NPC bodies.</param>
		/// <param name="neighbourKeys">Each neighbour's identity key, in the same order.</param>
		/// <param name="radius">Distance at which a neighbour starts to push, in metres.</param>
		/// <param name="maxSpeed">Push speed when fully overlapped, in metres per second.</param>
		/// <returns>A horizontal velocity, zero when nothing is inside the radius.</returns>
		public static Vector3 Resolve(Vector3 self, uint selfKey, IReadOnlyList<Vector3> neighbours, IReadOnlyList<uint> neighbourKeys, float radius, float maxSpeed)
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
					/* Exactly on top of each other: any direction is out, but the two bodies must
					 * not pick the SAME one. A fixed +x did exactly that: both pushed the same way,
					 * slid together at the push speed to a NavMesh edge and stayed stacked there —
					 * and a spawner without random placement stacks every NPC it places on one
					 * point. Each pair gets its own axis from both keys, and the lower key takes
					 * one end of it and the higher the other, so the pair separates; three or more
					 * stacked bodies spread along different axes. */
					uint otherKey = neighbourKeys != null && i < neighbourKeys.Count ? neighbourKeys[i] : selfKey;
					away = CoincidentPushDirection(selfKey, otherKey);
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

		/// <summary>
		/// The horizontal direction <paramref name="selfKey"/> pushes in when its body coincides
		/// with <paramref name="otherKey"/>'s.
		/// </summary>
		/// <remarks>
		/// The pair's axis depends only on the unordered pair, and the lower key pushes along it
		/// while the higher pushes against it, so the two answers are exact opposites. Equal keys —
		/// which distinct NPCs never have, see <see cref="AIController.IdentityKey"/> — fall back
		/// to a fixed direction.
		/// </remarks>
		/// <param name="selfKey">The pushing body's key.</param>
		/// <param name="otherKey">The coincident body's key.</param>
		/// <returns>A unit horizontal direction.</returns>
		public static Vector3 CoincidentPushDirection(uint selfKey, uint otherKey)
		{
			if (selfKey == otherKey)
			{
				return Vector3.right;
			}

			uint low = selfKey < otherKey ? selfKey : otherKey;
			uint high = selfKey < otherKey ? otherKey : selfKey;
			float angle = AIController.PhaseFraction(low, high) * (2f * Mathf.PI);
			Vector3 axis = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
			return selfKey == low ? axis : -axis;
		}
	}
}
