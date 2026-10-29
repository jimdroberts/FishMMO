#if UNITY_EDITOR
using System;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How a way crosses a river (decision 35 (i), 2026-10-18).</summary>
	public enum PathCrossingKind : byte
	{
		/// <summary>Waded where the river is no deeper than the way's class fords (<see cref="PathNetworkPlanner.Wades"/>).</summary>
		Ford = 0,
		/// <summary>A bridge site laid over it (<see cref="PathBridgeSite"/>).</summary>
		Bridge = 1,
		/// <summary>Too wide for the way to bridge: a rope ferry, landings on both banks (<see cref="PathFerrySite"/>).</summary>
		Ferry = 2,
	}

	/// <summary>A rope ferry a way needs where a river is wider than its class would bridge (<see cref="PathCrossings"/>).</summary>
	public sealed class PathFerrySite
	{
		public int PathId;
		/// <summary>The middle of the crossing, at the higher bank's height.</summary>
		public Vector3 Centre;
		/// <summary>Degrees about +y the rope runs along (across the river).</summary>
		public float Yaw;
		/// <summary>The water's width under the rope, metres.</summary>
		public float Span;
		/// <summary>Where the way steps off at each bank (the landings' ramps), world.</summary>
		public Vector3 LandingA, LandingB;
	}

	/// <summary>
	/// The pure choices of a way's river crossing (decision 35 (i), 2026-10-18): ford, bridge or rope ferry for each way by its class
	/// and the river's width there, and whose biome a bridge belongs to — its ENDS, the banks it joins, not the water under its middle.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Bridges belong to the towns they join.</b> A bridge site took the biome at its centre, which over a river 8 m and more
	/// wide is the River biome (a cell is the River's when most of it is river), so every such bridge was built in the River's
	/// palette and variants whatever country it stood in. It now takes the commonest land biome along both approaches
	/// (<see cref="BridgeBiome"/>), a water-role biome only when no land answers.
	/// </para>
	/// <para>
	/// <b>Ferries are a seam until their pieces exist.</b> <see cref="Choose"/> and <see cref="FerryOverMetres"/> say where a way
	/// would take a rope ferry (a footpath over a broad river, any way over one wider than seven 32 m arches), and the planner flags
	/// such a crossing <see cref="ScenePathPointFlags.Ferry"/> and records a <see cref="PathFerrySite"/> when its input asks
	/// (<see cref="PathPlanInput.FerryOver"/>). The stage leaves it unset — every crossing is a ford or a bridge as before — until
	/// the ferry flat, its rope posts, bell-post and landings are generated (the River round's signature pieces, I7).
	/// </para>
	/// </remarks>
	public static class PathCrossings
	{
		/// <summary>The longest crossing a road or a highway bridges: seven arches of 32 m (the River round's "stone arches").</summary>
		public const float MaxBridgeSpanMetres = 224f;

		/// <summary>How far along each approach a bridge's biome is read, metres apart, from its abutment out (<see cref="BridgeBiome"/>).</summary>
		public const float ApproachStepMetres = 6f;

		/// <summary>The approach samples each side.</summary>
		public const int ApproachSamples = 3;

		/// <summary>How far past the water's edge a bridge's deck steps off onto its abutment (PathNetworkPlanner's own figure).</summary>
		public const float AbutmentMetres = 2.5f;

		/// <summary>Ford, bridge or ferry: a ford where the way wades it; a ferry where the river is wider than the way bridges (<paramref name="ferryOver"/>, metres; infinity never); else a bridge.</summary>
		public static PathCrossingKind Choose(bool wades, float width, float ferryOver)
		{
			if (wades)
			{
				return PathCrossingKind.Ford;
			}
			return width > ferryOver ? PathCrossingKind.Ferry : PathCrossingKind.Bridge;
		}

		/// <summary>
		/// The widest river a way of a class bridges before it takes a ferry, metres: a trail or a footpath is no one's to bridge over
		/// a broad river (40 m), a cart track's village a modest one (80 m), a road or a highway up to <see cref="MaxBridgeSpanMetres"/>.
		/// A town's street and the yards are never routed over a river.
		/// </summary>
		public static float FerryOverMetres(ScenePathClass way)
		{
			switch (way)
			{
				case ScenePathClass.Trail:
				case ScenePathClass.Footpath:
					return 40f;
				case ScenePathClass.CartTrack:
				case ScenePathClass.AncientRoad:
					return 80f;
				case ScenePathClass.Road:
				case ScenePathClass.Highway:
					return MaxBridgeSpanMetres;
				default:
					return float.PositiveInfinity;
			}
		}

		/// <summary>
		/// The biome a bridge belongs to: the commonest LAND biome along its two approaches (<see cref="ApproachSamples"/> points each
		/// side, <see cref="ApproachStepMetres"/> apart, from where the deck steps off), ties to the lower index; a water-role biome
		/// (River, Lake…) only where no land biome answers on either bank; the biome at its centre where nothing answers at all.
		/// </summary>
		/// <param name="centre">The crossing's middle (world).</param>
		/// <param name="yaw">Degrees about +y the deck runs along.</param>
		/// <param name="span">The water's width under it, metres.</param>
		/// <param name="biomeAt">The dominant biome's index at (x, z), −1 for none.</param>
		/// <param name="isWater">True for a water-role biome's index.</param>
		/// <param name="biomeCount">How many biomes there are.</param>
		public static int BridgeBiome(Vector3 centre, float yaw, float span, Func<float, float, int> biomeAt, Func<int, bool> isWater, int biomeCount)
		{
			if (biomeAt == null || biomeCount <= 0)
			{
				return -1;
			}
			float yr = yaw * Mathf.Deg2Rad;
			var axis = new Vector2(Mathf.Sin(yr), Mathf.Cos(yr));
			var mid = new Vector2(centre.x, centre.z);
			var land = new int[biomeCount];
			var water = new int[biomeCount];
			for (int side = -1; side <= 1; side += 2)
			{
				for (int k = 0; k < ApproachSamples; k++)
				{
					Vector2 at = mid + axis * (side * (span * 0.5f + AbutmentMetres + k * ApproachStepMetres));
					int b = biomeAt(at.x, at.y);
					if (b < 0 || b >= biomeCount)
					{
						continue;
					}
					if (isWater != null && isWater(b))
					{
						water[b]++;
					}
					else
					{
						land[b]++;
					}
				}
			}
			int best = Commonest(land);
			if (best < 0)
			{
				best = Commonest(water);
			}
			return best >= 0 ? best : biomeAt(centre.x, centre.z);
		}

		/// <summary>True for the roles of the water's own biomes (a lake's, a river's, an estuary's): a bridge's end is never one of these while land answers. Salt flats and ice sheets are ground.</summary>
		public static bool IsWaterBiome(BiomeWaterRole role)
		{
			return role == BiomeWaterRole.Lake || role == BiomeWaterRole.MethaneLake || role == BiomeWaterRole.River || role == BiomeWaterRole.Estuary;
		}

		// ── Crossing styles (2026-10-26, phase I2l-b) ───────────────

		/// <summary>
		/// The water widths a crossing style bridges (metres, min, max): the liana bridge a jungle river a vine cable reaches over, the
		/// corbelled laterite bridge as many narrow openings as a royal road needs, the covered wind-and-rain gallery a broad karst river
		/// on its piers, the cantilever a gap its log cribs close, the Goblins' Bridge a stream its slabs lie over, a snow bridge only a
		/// narrow channel. Zero for the kit's bridge (it takes every span).
		/// </summary>
		public static Vector2 StyleSpan(BiomeCrossingStyle style)
		{
			switch (style)
			{
				case BiomeCrossingStyle.Liana: return new Vector2(6f, 40f);
				case BiomeCrossingStyle.CorbelledLaterite: return new Vector2(4f, 60f);
				case BiomeCrossingStyle.WindAndRain: return new Vector2(8f, 96f);
				case BiomeCrossingStyle.Cantilever: return new Vector2(6f, 30f);
				case BiomeCrossingStyle.GoblinsBridge: return new Vector2(2f, 20f);
				case BiomeCrossingStyle.SnowBridge: return new Vector2(1f, 10f);
				default: return Vector2.zero;
			}
		}

		/// <summary>
		/// True when a river crossing of <paramref name="span"/> metres is built in the style: a set style, a span within
		/// <see cref="StyleSpan"/>, and not a gorge (crossed by one stone arch or rope bridge lip to lip, PropsFeature.LayGorgeBridge).
		/// Anything else takes the kit's bridge, as before.
		/// </summary>
		public static bool StyleFits(BiomeCrossingStyle style, float span, bool gorge)
		{
			if (style == BiomeCrossingStyle.Default || gorge || !(span > 0f))
			{
				return false;
			}
			Vector2 range = StyleSpan(style);
			return span >= range.x && span <= range.y;
		}

		/// <summary>The Bridge template variant a style is built from (PointOfInterestTemplateSpecs.Works); null for the kit's (the seeded pick).</summary>
		public static string StyleVariant(BiomeCrossingStyle style)
		{
			switch (style)
			{
				case BiomeCrossingStyle.Liana: return "Liana";
				case BiomeCrossingStyle.CorbelledLaterite: return "Corbelled Laterite";
				case BiomeCrossingStyle.WindAndRain: return "Wind and Rain";
				case BiomeCrossingStyle.Cantilever: return "Cantilever";
				case BiomeCrossingStyle.GoblinsBridge: return "Goblins Bridge";
				case BiomeCrossingStyle.SnowBridge: return "Snow Bridge";
				default: return null;
			}
		}

		/// <summary>The structure piece a style's span is laid from (StructurePieces.Crossings); null for the kit's.</summary>
		public static string StylePiece(BiomeCrossingStyle style)
		{
			switch (style)
			{
				case BiomeCrossingStyle.Liana: return "LianaBridge";
				case BiomeCrossingStyle.CorbelledLaterite: return "LateriteSpan";
				case BiomeCrossingStyle.WindAndRain: return "CoveredSpan";
				case BiomeCrossingStyle.Cantilever: return "CantileverBridge";
				case BiomeCrossingStyle.GoblinsBridge: return "GoblinSlab";
				case BiomeCrossingStyle.SnowBridge: return "SnowBridge";
				default: return null;
			}
		}

		/// <summary>The water a Bridge site of <paramref name="radius"/> was sited over, metres (the inverse of PathBridgeSite.RecordRadius, at least 2 m).</summary>
		public static float SpanOfRecord(float radius) => Mathf.Max(2f, 2f * (radius - 6f));

		private static int Commonest(int[] votes)
		{
			int best = -1;
			for (int b = 0; b < votes.Length; b++)
			{
				if (votes[b] > 0 && (best < 0 || votes[b] > votes[best]))
				{
					best = b;
				}
			}
			return best;
		}
	}
}
#endif
