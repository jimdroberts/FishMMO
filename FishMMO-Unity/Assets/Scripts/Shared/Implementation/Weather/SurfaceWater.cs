using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The open water in the loaded scene — a sea, its lakes and rivers: where anything falling out of the
	/// sky stops, whether a point is under a surface, and how high the water stands at a place.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A sea has nothing to hit.</b> The weather finds the highest surface around the camera by
	/// casting rays down, and a ray goes straight through water to the sea bed. So rain and snow
	/// fell through the sea and landed on the sand under it, splashes burst on the bottom, and the
	/// lens kept its raindrops with the camera three metres down. Wherever the ground is lower than
	/// the water, the water is the highest surface.
	/// </para>
	/// <para>
	/// <b>Published by the water, read by the weather.</b> The water depends on the game and never the
	/// other way round, so the weather cannot ask it — each body of water registers itself here, the
	/// way the server registers its weather host into <see cref="WeatherQuery.Commands"/>.
	/// </para>
	/// <para>
	/// <b>One sea, any number of inland waters.</b> The sea stands at one level everywhere its ground is
	/// lower (<see cref="TryGetLevel"/>). A lake or a river stands only where it is
	/// (<see cref="IAreaSource"/>), so the question that covers both is the water at a place:
	/// <see cref="TryGetSurfaceAt"/>.
	/// </para>
	/// </remarks>
	public static class SurfaceWater
	{
		/// <summary>What a body of open water tells the weather about itself.</summary>
		public interface ISource
		{
			/// <summary>The still surface, tide included, in world metres; negative infinity for water that has no one level (rivers).</summary>
			float Level { get; }

			/// <summary>The significant height of the waves on it, in metres.</summary>
			float WaveHeight { get; }

			/// <summary>True when a point is under the moving surface, waves and all.</summary>
			bool IsUnder(Vector3 point);
		}

		/// <summary>Water that stands only in some places: lakes and rivers.</summary>
		public interface IAreaSource : ISource
		{
			/// <summary>The water's surface over a world position, when there is water there.</summary>
			bool TryGetSurface(float x, float z, out float level);

			/// <summary>The water's horizontal velocity at a world position, m/s; zero where it is still or absent.</summary>
			Vector2 CurrentAt(float x, float z);
		}

		/// <summary>
		/// How far over a height a body's floor may lie and the height still be in or over that body, metres (2026-10-21): a
		/// swimmer's feet, a footstep, a camera stand at most this far under the bed of the water they are in.
		/// </summary>
		public const float FloorReach = 1f;

		/// <summary>
		/// Area water that stands at more than one level over a place (2026-10-21: a cave's stream under a lava tube's river on
		/// the level over it, a lake on the hill over both): which surface a height is in or over, not only the highest.
		/// </summary>
		public interface IStackedAreaSource : ILavaAreaSource
		{
			/// <summary>
			/// The surface of the water at a world position that a height stands in or over: the highest of those whose floor
			/// lies no more than <see cref="FloorReach"/> over the height. Water on a level above (its floor over the height, the
			/// rock between) is another place's.
			/// </summary>
			bool TryGetSurface(float x, float y, float z, out float level, out bool lava);
		}

		/// <summary>
		/// Area water some of which may be molten lava (a lava tube's river, a pit in its chamber): which the highest surface over a
		/// place is, so what lives, wades, splashes or floats in water can leave lava alone.
		/// </summary>
		public interface ILavaAreaSource : IAreaSource
		{
			/// <summary>The highest surface over a world position, when there is one there, and whether it is lava.</summary>
			bool TryGetSurface(float x, float z, out float level, out bool lava);
		}

		/// <summary>
		/// A sea that dims the daylight on its way down to its floor (2026-10-25, phase I2m, decision 36 (c)): how much per metre, the
		/// scene cut's (BiomeSeaFloors.DaylightExtinction); 0 for none.
		/// </summary>
		public interface IDaylightSource : ISource
		{
			/// <summary>Per metre of depth under the still surface: the daylight on the floor is exp(−this × depth).</summary>
			float DaylightExtinction { get; }
		}

		/// <summary>Water whose drawn surface moves (the sea's waves): what a floating body is set on to ride them.</summary>
		public interface IWaveSource : ISource
		{
			/// <summary>The drawn surface's height over a world position, waves and all, in world metres.</summary>
			float SurfaceAt(float x, float z);
		}

		private static readonly List<ISource> sources = new List<ISource>();

		/// <summary>Moves whenever a body of water joins or leaves: what a cache of answers about the water keys on.</summary>
		public static int Version { get; private set; }

		/// <summary>True for a source Unity destroyed without it unregistering: Unity's destroyed objects are not null references.</summary>
		private static bool Dead(ISource source) => source == null || (source is Object unityObject && unityObject == null);

		private static void Prune()
		{
			for (int i = sources.Count - 1; i >= 0; i--)
			{
				if (Dead(sources[i]))
				{
					sources.RemoveAt(i);
					Version++;
				}
			}
		}

		/// <summary>The sea: the registered water with one level everywhere, or null.</summary>
		private static ISource Sea
		{
			get
			{
				Prune();
				foreach (ISource source in sources)
				{
					if (!(source is IAreaSource))
					{
						return source;
					}
				}
				return null;
			}
		}

		/// <summary>True while the scene has any open water.</summary>
		public static bool Present
		{
			get
			{
				Prune();
				return sources.Count > 0;
			}
		}

		/// <summary>The sea's still surface, tide included, when there is a sea.</summary>
		public static bool TryGetLevel(out float level)
		{
			ISource sea = Sea;
			level = sea != null ? sea.Level : float.NegativeInfinity;
			return sea != null;
		}

		/// <summary>
		/// The highest still water surface over a world position: the sea's level, or a lake's or a river's
		/// where one stands. False where there is none — though the sea's level is returned anywhere when
		/// there is a sea, as it always was: whether the ground there is under it is the caller's to compare.
		/// </summary>
		public static bool TryGetSurfaceAt(float x, float z, out float level)
		{
			Prune();
			level = float.NegativeInfinity;
			bool any = false;
			foreach (ISource source in sources)
			{
				if (source is IAreaSource area)
				{
					if (area.TryGetSurface(x, z, out float here) && here > level)
					{
						level = here;
						any = true;
					}
				}
				else if (source.Level > level)
				{
					level = source.Level;
					any = true;
				}
			}
			return any;
		}

		/// <summary>
		/// The highest lake or river surface over a world position, leaving the sea out: for maps that bake
		/// what stands where, which must not bake the sea's level, since the tide moves it every frame.
		/// </summary>
		public static bool TryGetInlandSurfaceAt(float x, float z, out float level)
		{
			Prune();
			level = float.NegativeInfinity;
			bool any = false;
			foreach (ISource source in sources)
			{
				if (source is IAreaSource area && area.TryGetSurface(x, z, out float here) && here > level)
				{
					level = here;
					any = true;
				}
			}
			return any;
		}

		/// <summary>
		/// <see cref="TryGetInlandSurfaceAt(float, float, out float)"/>, and whether the highest inland surface there is molten lava
		/// (<see cref="ILavaAreaSource"/>) rather than water.
		/// </summary>
		public static bool TryGetInlandSurfaceAt(float x, float z, out float level, out bool lava)
		{
			Prune();
			level = float.NegativeInfinity;
			lava = false;
			bool any = false;
			foreach (ISource source in sources)
			{
				if (!(source is IAreaSource area))
				{
					continue;
				}
				bool molten = false;
				float here;
				bool found = area is ILavaAreaSource liquid ? liquid.TryGetSurface(x, z, out here, out molten) : area.TryGetSurface(x, z, out here);
				if (found && here > level)
				{
					level = here;
					lava = molten;
					any = true;
				}
			}
			return any;
		}

		/// <summary>The water's horizontal velocity at a world position, m/s: a river's current; zero on the sea, a lake or dry ground.</summary>
		public static Vector2 CurrentAt(float x, float z)
		{
			Prune();
			foreach (ISource source in sources)
			{
				if (source is IAreaSource area)
				{
					Vector2 current = area.CurrentAt(x, z);
					if (current.sqrMagnitude > 1e-8f)
					{
						return current;
					}
				}
			}
			return Vector2.zero;
		}

		/// <summary>How fast the sea dims the daylight with depth, per metre (<see cref="IDaylightSource"/>), or 0 with no sea or none set.</summary>
		public static float DaylightExtinction
		{
			get
			{
				ISource sea = Sea;
				return sea is IDaylightSource daylight ? Mathf.Max(0f, daylight.DaylightExtinction) : 0f;
			}
		}

		/// <summary>The significant height of the sea's waves, or 0 with no sea.</summary>
		public static float WaveHeight
		{
			get
			{
				ISource sea = Sea;
				return sea != null ? sea.WaveHeight : 0f;
			}
		}

		/// <summary>
		/// How far the drawn waves stand above (or below) the still water at a world position, metres: the sea's waves
		/// where the sea is the water there, 0 under a lake or a river standing higher, on calm water, or with no sea.
		/// </summary>
		/// <remarks>
		/// <b>Presentation only.</b> The movement floats on the still surface (<see cref="WaterQuery"/>, a synced tick and no
		/// waves, so the server and every client agree); a figure drawn on that line sits still while the sea heaves through
		/// it. Added to the still line, this is the drawn sea at the point: the figure rides the waves the player sees.
		/// It reads the frame's camera and clock, so it is never for anything simulated.
		/// </remarks>
		public static float WaveOffsetAt(float x, float z)
		{
			ISource sea = Sea;
			if (!(sea is IWaveSource waves))
			{
				return 0f;
			}
			float level = sea.Level;
			if (TryGetInlandSurfaceAt(x, z, out float inland) && inland > level)
			{
				return 0f;
			}
			float offset = waves.SurfaceAt(x, z) - level;
			return float.IsNaN(offset) || float.IsInfinity(offset) ? 0f : offset;
		}

		/// <summary>True when a point is under any water's moving surface.</summary>
		public static bool IsUnder(Vector3 point)
		{
			Prune();
			foreach (ISource source in sources)
			{
				if (source.IsUnder(point))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Adds a body of water to the scene's.</summary>
		public static void Register(ISource water)
		{
			if (water != null && !sources.Contains(water))
			{
				sources.Add(water);
				Version++;
			}
		}

		/// <summary>Withdraws a body of water.</summary>
		public static void Unregister(ISource water)
		{
			if (sources.Remove(water))
			{
				Version++;
			}
		}

		/// <summary>
		/// Forgets the water when play starts. With domain reload off a static survives from one
		/// session to the next, and a sea from the last one would go on stopping the rain.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnLoad()
		{
			sources.Clear();
			Version++;
		}
	}
}
