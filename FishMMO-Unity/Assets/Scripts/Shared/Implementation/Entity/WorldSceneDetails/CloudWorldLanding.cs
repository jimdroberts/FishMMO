using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where a character can be put down in a cloud world (<see cref="SceneCloudWorld"/>) and stay put: over an island,
	/// above the depth the deck takes whoever reaches it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pure: a world's islands and a scene's cached details in, a place out. The server asks it whenever it is about to
	/// put a character somewhere in a cloud world it did not walk to: a respawn at a bind point, a resurrect of a body the
	/// deck has, an immortal character fished back out of it.
	/// </para>
	/// <para>
	/// <b>Why a bind point is asked about at all.</b> Anywhere else a bind point is ground by construction, since a
	/// bindstone binds where the character stands. A cloud world's scene can be re-cut over an old one, and a bind point
	/// recorded on the old ground can be open air now; respawning there drops the character straight back into the
	/// deck, which kills it, which offers the same respawn again.
	/// </para>
	/// <para>
	/// An island's reach is its outline's widest, so "over an island" is generous at a ragged edge. That is the right way
	/// to be wrong here: every authored place on an island passes, and only open air between islands fails.
	/// </para>
	/// </remarks>
	public static class CloudWorldLanding
	{
		/// <summary>The kinds of authored place a landing is looked for among, in the order they are preferred.</summary>
		private static readonly RescuePointKind[] Preference =
		{
			RescuePointKind.Respawn,
			RescuePointKind.Spawn,
			RescuePointKind.Waypoint,
		};

		/// <summary>
		/// True when <paramref name="point"/> holds a character in <paramref name="world"/>: at or above its death altitude
		/// and inside an island's reach. Always true outside a cloud world.
		/// </summary>
		public static bool Holds(SceneCloudWorld world, Vector3 point)
		{
			if (world == null)
			{
				return true;
			}
			return point.y >= world.DeathAltitude && world.IslandAt(point.x, point.z) >= 0;
		}

		/// <summary>
		/// The nearest authored place in the scene that <see cref="Holds"/>: a respawn position first, since that is
		/// where a death goes, then an initial spawn position, then a waypoint.
		/// </summary>
		/// <remarks>
		/// When nothing the scene authored holds, the nearest respawn position is returned anyway: a cut that put every
		/// respawn over open air is a broken cut, and the place it meant is still a better answer than the deck.
		/// </remarks>
		/// <param name="world">The cloud world the character is in.</param>
		/// <param name="details">The cached details of that scene.</param>
		/// <param name="from">Where the character is, or where it was going to be put.</param>
		/// <param name="point">The place found.</param>
		public static bool TryFind(SceneCloudWorld world, WorldSceneDetails details, Vector3 from, out RescuePoint point)
			=> TryFind(position => Holds(world, position), details, from, out point);

		/// <summary>
		/// <see cref="TryFind(SceneCloudWorld, WorldSceneDetails, Vector3, out RescuePoint)"/> with what holds given as a
		/// test, so the choice can be exercised without a world.
		/// </summary>
		public static bool TryFind(Predicate<Vector3> holds, WorldSceneDetails details, Vector3 from, out RescuePoint point)
		{
			point = default;
			if (holds == null || details == null)
			{
				return false;
			}

			for (int k = 0; k < Preference.Length; ++k)
			{
				List<RescuePoint> points = RescuePoints.List(details, Preference[k]);
				float nearest = float.MaxValue;
				bool found = false;
				for (int i = 0; i < points.Count; ++i)
				{
					if (!holds(points[i].Position))
					{
						continue;
					}
					float distance = (points[i].Position - from).sqrMagnitude;
					if (distance < nearest)
					{
						nearest = distance;
						point = points[i];
						found = true;
					}
				}
				if (found)
				{
					return true;
				}
			}

			return RescuePoints.TryFind(details, RescuePointKind.Respawn, null, from, out point);
		}
	}
}
