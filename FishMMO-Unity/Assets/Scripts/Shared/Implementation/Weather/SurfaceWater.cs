using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The open water in the loaded scene: where anything falling out of the sky stops, and whether
	/// a point is under the surface.
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
	/// <b>Published by the water, read by the weather.</b> The water plugin depends on the game and
	/// never the other way round, so the weather cannot ask it — the water registers itself here,
	/// the way the server registers its weather host into <see cref="WeatherQuery.Commands"/>.
	/// </para>
	/// <para>
	/// One sea at a time, which is the water plugin's own limit.
	/// </para>
	/// </remarks>
	public static class SurfaceWater
	{
		/// <summary>What a body of open water tells the weather about itself.</summary>
		public interface ISource
		{
			/// <summary>The still surface, tide included, in world metres.</summary>
			float Level { get; }

			/// <summary>The significant height of the waves on it, in metres.</summary>
			float WaveHeight { get; }

			/// <summary>True when a point is under the moving surface, waves and all.</summary>
			bool IsUnder(Vector3 point);
		}

		private static ISource source;

		/// <summary>
		/// The registered water, or null. A water that was destroyed without being unregistered is
		/// treated as gone: Unity's destroyed objects are not null references.
		/// </summary>
		private static ISource Live => source is Object unityObject && unityObject == null ? null : source;

		/// <summary>True while the scene has open water.</summary>
		public static bool Present => Live != null;

		/// <summary>The still surface, tide included, when there is water.</summary>
		public static bool TryGetLevel(out float level)
		{
			ISource water = Live;
			level = water != null ? water.Level : float.NegativeInfinity;
			return water != null;
		}

		/// <summary>The significant height of the waves, or 0 with no water.</summary>
		public static float WaveHeight
		{
			get
			{
				ISource water = Live;
				return water != null ? water.WaveHeight : 0f;
			}
		}

		/// <summary>True when a point is under the water's moving surface.</summary>
		public static bool IsUnder(Vector3 point)
		{
			ISource water = Live;
			return water != null && water.IsUnder(point);
		}

		/// <summary>Makes a body of water the scene's.</summary>
		public static void Register(ISource water)
		{
			source = water;
		}

		/// <summary>Withdraws a body of water, if it is the one registered.</summary>
		public static void Unregister(ISource water)
		{
			if (ReferenceEquals(source, water))
			{
				source = null;
			}
		}

		/// <summary>
		/// Forgets the water when play starts. With domain reload off a static survives from one
		/// session to the next, and a sea from the last one would go on stopping the rain.
		/// </summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnLoad()
		{
			source = null;
		}
	}
}
