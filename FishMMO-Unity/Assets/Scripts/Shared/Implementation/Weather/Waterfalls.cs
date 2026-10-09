using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// The waterfalls in the loaded scenes: where each goes over, where it lands and how much power it carries, for
	/// everything round a fall that is not the water itself — the mist it raises in the fog, the rock its spray wets,
	/// the roar it makes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Published by the water, read by the weather and the audio.</b> The falls are built by the water plugin
	/// (FishMMO.Water's InlandWaterRenderer), which the client's weather and audio do not reference; as with
	/// <see cref="SurfaceWater"/>, the water registers what it built here and they ask.
	/// </para>
	/// <para>
	/// <b>One measure of size.</b> A fall's mist, wet zone and loudness all scale with its <see cref="Fall.Power"/>
	/// (ρgQH, the energy it sheds a second), through <see cref="PlumeRadius"/>, so a trickle off a high ledge and a
	/// river over a low one are each as big as their water makes them.
	/// </para>
	/// </remarks>
	public static class Waterfalls
	{
		/// <summary>One fall, or one separate part of a fall that a ledge has split.</summary>
		public struct Fall
		{
			/// <summary>The middle of the lip, world metres.</summary>
			public Vector3 Lip;
			/// <summary>Where the water lands in the pool, world metres.</summary>
			public Vector3 Landing;
			/// <summary>The river's way downstream at the fall, horizontal and unit length.</summary>
			public Vector3 Downstream;
			/// <summary>How wide the water is where it goes over, metres.</summary>
			public float Width;
			/// <summary>How far it falls, metres.</summary>
			public float Drop;
			/// <summary>The water going over, m³/s.</summary>
			public float Discharge;
			/// <summary>The power it sheds, watts: ρ g Q H.</summary>
			public float Power;
			/// <summary>How far down the water stays one sheet before it breaks into jets, metres.</summary>
			public float BreakupLength;
			/// <summary>How thick the water is where it lands, metres (B_j).</summary>
			public float ImpactThickness;
			/// <summary>How far out from where it lands it is thrown and boils, metres.</summary>
			public float ImpactRadius;
		}

		private static readonly Dictionary<object, Fall[]> published = new Dictionary<object, Fall[]>();
		private static readonly List<Fall> all = new List<Fall>();
		private static bool stale;

		/// <summary>Bumped whenever the falls change, so a reader can keep what it built from them until then.</summary>
		public static int Version { get; private set; }

		/// <summary>Every fall in the loaded scenes.</summary>
		public static IReadOnlyList<Fall> All
		{
			get
			{
				Prune();
				if (stale)
				{
					all.Clear();
					foreach (Fall[] falls in published.Values)
					{
						all.AddRange(falls);
					}
					stale = false;
				}
				return all;
			}
		}

		/// <summary>Publishes <paramref name="owner"/>'s falls, replacing what it published before; null or none removes them.</summary>
		public static void Publish(object owner, IReadOnlyList<Fall> falls)
		{
			if (owner == null)
			{
				return;
			}
			if (falls == null || falls.Count == 0)
			{
				Withdraw(owner);
				return;
			}
			var copy = new Fall[falls.Count];
			for (int i = 0; i < copy.Length; i++)
			{
				copy[i] = falls[i];
			}
			published[owner] = copy;
			stale = true;
			Version++;
		}

		/// <summary>Removes <paramref name="owner"/>'s falls.</summary>
		public static void Withdraw(object owner)
		{
			if (owner != null && published.Remove(owner))
			{
				stale = true;
				Version++;
			}
		}

		/// <summary>Drops the falls of an owner Unity destroyed without withdrawing them (its reference is not null).</summary>
		private static void Prune()
		{
			List<object> dead = null;
			foreach (object owner in published.Keys)
			{
				if (owner is Object unityObject && unityObject == null)
				{
					(dead ??= new List<object>()).Add(owner);
				}
			}
			if (dead != null)
			{
				foreach (object owner in dead)
				{
					published.Remove(owner);
				}
				stale = true;
				Version++;
			}
		}

		/// <summary>The density of water, kg/m³, and gravity, m/s².</summary>
		public const float WaterDensity = 1000f, Gravity = 9.81f;

		/// <summary>The power of water falling: ρ g Q H, watts.</summary>
		public static float PowerOf(float discharge, float drop) => WaterDensity * Gravity * Mathf.Max(0f, discharge) * Mathf.Max(0f, drop);

		/// <summary>
		/// How far the mist a fall raises spreads from where it lands, metres. As the cube root of its power, the size
		/// of the turbulent cloud a steady source of energy keeps stirred: about 17 m for a 1.3 MW mountain fall
		/// (4 m wide, 50 m high), about 200 m for Niagara's couple of gigawatts. Never less than half its width. (0.1 P^⅓
		/// drew a cloud too small to hide a landing in, Jim 2026-10-08.)
		/// </summary>
		public static float PlumeRadius(in Fall fall)
		{
			// And a fifth of its height besides: a taller fall throws its spray further and its cloud stands higher (Jim, 2026-10-08).
			return Mathf.Clamp(0.16f * Mathf.Pow(Mathf.Max(1f, fall.Power), 1f / 3f), 3f, 200f) + 0.5f * fall.Width + 0.2f * Mathf.Max(0f, fall.Drop);
		}

		/// <summary>How high the mist stands over where the fall lands, metres: a plume rises higher than it spreads.</summary>
		public static float PlumeHeight(in Fall fall) => 1.5f * PlumeRadius(fall);

		/// <summary>How far round where it lands, and round the curtain, its spray wets the ground and the rock, metres.</summary>
		public static float WetRadius(in Fall fall) => 1.2f * PlumeRadius(fall);
	}
}
