using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.Weather
{
	/// <summary>What a body of water is, to anything that swims, wades or drowns in it.</summary>
	public enum WaterBody : byte
	{
		None = 0,
		Sea = 1,
		Lake = 2,
		River = 3,
		/// <summary>Molten lava (a lava sea, or a lava tube's river or pit): swum by the fire-proof, deadly to anyone else.</summary>
		Lava = 4,
	}

	/// <summary>The water over a place at a tick.</summary>
	public struct WaterSample
	{
		/// <summary>What stands here; <see cref="WaterBody.None"/> where nothing does.</summary>
		public WaterBody Body;

		/// <summary>The still surface, world metres; negative infinity where there is no water.</summary>
		public float Surface;

		/// <summary>The water's horizontal velocity, m/s: a river's current; zero on a lake or the sea.</summary>
		public Vector2 Current;

		/// <summary>True where any water stands over the place (though the ground may still stand above it: compare).</summary>
		public bool Present => Body != WaterBody.None;

		/// <summary>How far under the surface a height is, metres; negative above it.</summary>
		public float DepthOf(float y) => Present ? Surface - y : float.NegativeInfinity;

		public static WaterSample Dry => new WaterSample { Body = WaterBody.None, Surface = float.NegativeInfinity };
	}

	/// <summary>
	/// The water of a scene, for the simulation: where a character swims, how deep an agent would have to go,
	/// what drowns or burns. Keyed by scene, as <see cref="WeatherQuery"/> is, and answered at a synced server tick,
	/// so the server and a predicting client get the identical answer.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Not <see cref="SurfaceWater"/>.</b> That is the weather's view of the scene being drawn: global, with no
	/// scene key (a scene server runs many scenes at once), and its sea moves with the frame's clock and its waves.
	/// This answers from what both peers hold alike: the sea's <see cref="SeaTide"/> at a tick, and the scene's lakes
	/// and rivers (<c>SceneWaterBodies</c>), which never move.
	/// </para>
	/// <para>
	/// <b>The sea is everywhere.</b> A sea's level is returned at any place in its scene: whether the ground there
	/// stands above it is the caller's to compare, since a character knows its ground and this does not. A lake or a
	/// river stands only where it is, and the highest water over a place answers. An inland lake or river of lava
	/// (<see cref="SurfaceWater.ILavaAreaSource"/>) answers as <see cref="WaterBody.Lava"/>, as a lava sea does.
	/// </para>
	/// </remarks>
	public static class WaterQuery
	{
		private sealed class SceneWaters
		{
			public SeaTide Sea;
			public bool SeaIsLava;
			public readonly List<SurfaceWater.IAreaSource> Inland = new List<SurfaceWater.IAreaSource>(1);

			public bool Empty => Sea == null && Inland.Count == 0;
		}

		private static readonly Dictionary<int, SceneWaters> scenes = new Dictionary<int, SceneWaters>();

		private static SceneWaters For(Scene scene, bool create)
		{
			if (!scenes.TryGetValue(scene.handle, out SceneWaters waters) && create)
			{
				waters = new SceneWaters();
				scenes[scene.handle] = waters;
			}
			return waters;
		}

		private static void Release(Scene scene, SceneWaters waters)
		{
			if (waters != null && waters.Empty)
			{
				scenes.Remove(scene.handle);
			}
		}

		/// <summary>The sea of <paramref name="scene"/>: its tide, and whether it is lava.</summary>
		public static void RegisterSea(Scene scene, SeaTide sea, bool lava)
		{
			if (sea == null)
			{
				return;
			}
			SceneWaters waters = For(scene, true);
			waters.Sea = sea;
			waters.SeaIsLava = lava;
		}

		public static void UnregisterSea(Scene scene, SeaTide sea)
		{
			SceneWaters waters = For(scene, false);
			if (waters != null && waters.Sea == sea)
			{
				waters.Sea = null;
				Release(scene, waters);
			}
		}

		/// <summary>Lakes and rivers standing in <paramref name="scene"/>.</summary>
		public static void RegisterInland(Scene scene, SurfaceWater.IAreaSource inland)
		{
			if (inland == null)
			{
				return;
			}
			SceneWaters waters = For(scene, true);
			if (!waters.Inland.Contains(inland))
			{
				waters.Inland.Add(inland);
			}
		}

		public static void UnregisterInland(Scene scene, SurfaceWater.IAreaSource inland)
		{
			SceneWaters waters = For(scene, false);
			if (waters != null && waters.Inland.Remove(inland))
			{
				Release(scene, waters);
			}
		}

		public static void Clear() => scenes.Clear();

		/// <summary>True when <paramref name="scene"/> has any water at all.</summary>
		public static bool HasWater(Scene scene) => scenes.ContainsKey(scene.handle);

		/// <summary>The sea of <paramref name="scene"/>, when it has one.</summary>
		public static bool TryGetSea(Scene scene, out SeaTide sea, out bool lava)
		{
			SceneWaters waters = For(scene, false);
			sea = waters?.Sea;
			lava = waters != null && waters.SeaIsLava;
			return sea != null;
		}

		/// <summary>The water over a place in <paramref name="scene"/> at a synced server tick: the highest surface standing there.</summary>
		public static WaterSample Sample(Scene scene, float x, float z, uint tick) => Sample(scene, x, float.PositiveInfinity, z, tick);

		/// <summary>
		/// The water a height stands in or over at a place in <paramref name="scene"/> at a synced server tick (2026-10-21): the
		/// highest surface of the lakes and rivers whose floor lies under it (<see cref="SurfaceWater.IStackedAreaSource"/>), so a
		/// swimmer in a cave's stream is not under a lava tube's river on the level over it. Positive infinity for
		/// <paramref name="y"/> is the highest over the place.
		/// </summary>
		public static WaterSample Sample(Scene scene, float x, float y, float z, uint tick)
		{
			WaterSample sample = WaterSample.Dry;
			SceneWaters waters = For(scene, false);
			if (waters == null)
			{
				return sample;
			}
			if (waters.Sea != null)
			{
				sample.Body = waters.SeaIsLava ? WaterBody.Lava : WaterBody.Sea;
				sample.Surface = waters.Sea.LevelAt(tick);
			}
			for (int i = waters.Inland.Count - 1; i >= 0; i--)
			{
				SurfaceWater.IAreaSource inland = waters.Inland[i];
				if (inland == null || (inland is Object unityObject && unityObject == null))
				{
					waters.Inland.RemoveAt(i);
					continue;
				}
				bool lava = false;
				float level;
				bool stacked = !float.IsPositiveInfinity(y) && inland is SurfaceWater.IStackedAreaSource;
				bool found = stacked ? ((SurfaceWater.IStackedAreaSource)inland).TryGetSurface(x, y, z, out level, out lava)
					: inland is SurfaceWater.ILavaAreaSource liquid ? liquid.TryGetSurface(x, z, out level, out lava) : inland.TryGetSurface(x, z, out level);
				if (found && level > sample.Surface)
				{
					/* The scene's lakes and rivers answer CurrentAt by asking TryGetSurface again first, which this has just
					 * done: their flow straight away is the same answer without the second lookup. */
					Vector2 current = inland is FishMMO.Shared.Biomes.SceneWaterBodies bodies
						? bodies.FlowAt(x, z, out _, out _, lava, y)
						: inland.CurrentAt(x, z);
					// A lava tube's river or pit is the lava liquid, as a lava sea is: it burns, and only the fire-proof swim it.
					sample.Body = lava ? WaterBody.Lava : current.sqrMagnitude > 1e-8f ? WaterBody.River : WaterBody.Lake;
					sample.Surface = level;
					sample.Current = current;
				}
			}
			return sample;
		}

		/// <summary>The water a position stands in or over now (<see cref="WeatherQuery.CurrentTick"/>): for anything not predicted.</summary>
		public static WaterSample Sample(Scene scene, Vector3 position) => Sample(scene, position.x, position.y, position.z, WeatherQuery.CurrentTick);
	}
}
