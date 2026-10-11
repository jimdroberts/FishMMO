#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Everything a site asks of a piece source for one piece: the slot's query, the style, the seed, and how worn and weathered it is.</summary>
	public struct PointOfInterestPieceRequest
	{
		/// <summary>The slot's query (<see cref="StructureKitPieceSource"/> reads "tag", "tag+tag!tag" and "=PieceId").</summary>
		public string Tag;
		public string Style;
		public int Seed;
		/// <summary>0 whole … 1 rubble: a source with ruined versions of its pieces may answer one past <see cref="StructureKitPieceSource.RuinDecay"/>.</summary>
		public float Decay;
		public StructureFinish Finish;
		/// <summary>True only in a scene stored under Assets/LOCAL (LocalArtScope): LOCAL stand-ins may then replace generated pieces.</summary>
		public bool AllowLocal;

		public PointOfInterestPieceRequest(string tag, string style, int seed, float decay = 0f, StructureFinish finish = StructureFinish.None, bool allowLocal = false)
		{
			Tag = tag;
			Style = style;
			Seed = seed;
			Decay = decay;
			Finish = finish;
			AllowLocal = allowLocal;
		}
	}

	/// <summary>How big a piece is, as a layout needs it before any art exists.</summary>
	public struct PointOfInterestPieceMetrics
	{
		/// <summary>Width along the piece's x and depth along its z, metres.</summary>
		public Vector2 Footprint;
		/// <summary>Length one module covers when laid end to end (a wall, a pier, a bridge span); 0 for a free-standing piece.</summary>
		public float Module;
		/// <summary>How far its foundation, plinth or piles reach below its origin, metres.</summary>
		public float Depth;
		public float Height;
		/// <summary>Its origin is a deck (a pier, a bridge span), not the ground line.</summary>
		public bool Deck;

		/// <summary>What a piece no source can measure is taken as: a 3 m square on the ground.</summary>
		public static PointOfInterestPieceMetrics Unknown => new PointOfInterestPieceMetrics { Footprint = new Vector2(3f, 3f), Height = 3f };
	}

	/// <summary>
	/// A piece source that also takes a site's weathering and decay and can say how big its pieces are. Plain
	/// <see cref="IStructurePieceSource"/>s (hand-made pieces) are still asked through their own Resolve.
	/// </summary>
	public interface IStructurePieceRequestSource : IStructurePieceSource
	{
		GameObject Resolve(in PointOfInterestPieceRequest request);
		bool TryMeasure(in PointOfInterestPieceRequest request, out PointOfInterestPieceMetrics metrics);
	}

	/// <summary>
	/// Resolves and measures pieces through <see cref="PointOfInterestPieces"/>' sources in priority order, giving the
	/// richer request to the sources that take one.
	/// </summary>
	public static class PointOfInterestPieceResolver
	{
		/// <summary>The first prefab any source has for the request, or null.</summary>
		public static GameObject Resolve(in PointOfInterestPieceRequest request)
		{
			if (string.IsNullOrEmpty(request.Tag))
			{
				return null;
			}
			StructureKitPieceSource.Register();
			foreach (IStructurePieceSource source in PointOfInterestPieces.Sources)
			{
				GameObject prefab = source is IStructurePieceRequestSource rich
					? rich.Resolve(request)
					: source.Resolve(request.Tag, request.Style ?? string.Empty, request.Seed);
				if (prefab != null)
				{
					return prefab;
				}
			}
			return null;
		}

		/// <summary>
		/// The piece's size by the first source that can measure it; <see cref="PointOfInterestPieceMetrics.Unknown"/>
		/// when none can. Measured from the kit's table, not the art, so a site lays the same before the art exists.
		/// </summary>
		public static PointOfInterestPieceMetrics Measure(in PointOfInterestPieceRequest request)
		{
			StructureKitPieceSource.Register();
			foreach (IStructurePieceSource source in PointOfInterestPieces.Sources)
			{
				if (source is IStructurePieceRequestSource rich && rich.TryMeasure(request, out PointOfInterestPieceMetrics metrics))
				{
					return metrics;
				}
			}
			return PointOfInterestPieceMetrics.Unknown;
		}
	}

	/// <summary>
	/// The procedural structure kit (<see cref="StructureKit"/>) as a piece source for the POI templates: the lowest
	/// priority, so a LOCAL or hand-made source registered above it wins for whatever it has (Jim, 2026-10-10).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Queries.</b> A slot's tag is read as <c>tag</c>, <c>tag+tag</c> (every tag), <c>tag!tag</c> (not that tag),
	/// or <c>=PieceId</c> (exactly that piece). Several kit tags span very different pieces ("tower" is a watchtower, a
	/// wall tower, a keep and a lighthouse), so templates narrow them rather than the kit growing a tag per use.
	/// </para>
	/// <para>
	/// <b>Intact first.</b> A query that does not ask for <c>ruin</c> or <c>stilt</c> takes the pieces without those
	/// tags when it has any: "house" is a whole house on the ground, and ruins come from decay instead. A piece laid
	/// past <see cref="RuinDecay"/> is swapped for the kit's ruin of it when there is one (a wall for a ruined wall),
	/// so a sunken city's walls read as fallen without the template naming ruins.
	/// </para>
	/// <para>
	/// <b>Style.</b> "timber", "stone", "hide", "iron" or "earth" picks among the pieces of that style when any has the
	/// tags, else among them all (a stone town's market stalls are still timber). Anything else (a race key) is any style.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public sealed class StructureKitPieceSource : IStructurePieceRequestSource
	{
		/// <summary>Below every other source: a hand-made piece always wins.</summary>
		public const int KitPriority = -1000;

		/// <summary>Decay past which a piece is laid as its ruin, where the kit has one.</summary>
		public const float RuinDecay = 0.5f;

		public static readonly StructureKitPieceSource Instance = new StructureKitPieceSource();

		static StructureKitPieceSource()
		{
			Register();
		}

		/// <summary>Registers the kit (once). Safe to call from tests and batch runs.</summary>
		public static void Register() => PointOfInterestPieces.Register(Instance);

		public int Priority => KitPriority;

		public GameObject Resolve(string tag, string style, int seed) => Resolve(new PointOfInterestPieceRequest(tag, style, seed));

		public GameObject Resolve(in PointOfInterestPieceRequest request)
		{
			StructurePiece piece = PieceFor(request.Tag, request.Style, request.Seed, request.Decay);
			if (piece == null)
			{
				return null;
			}
			int variant = StructureKit.PickVariant(piece, request.Seed);
			GameObject prefab = StructureKit.Prefab(piece.Id, variant, request.Finish, request.AllowLocal);
			if (prefab == null && request.Finish != StructureFinish.None)
			{
				// Art generated before the finishes were: the plain piece rather than nothing.
				prefab = StructureKit.Prefab(piece.Id, variant, StructureFinish.None, request.AllowLocal);
			}
			return prefab;
		}

		public bool TryMeasure(in PointOfInterestPieceRequest request, out PointOfInterestPieceMetrics metrics)
		{
			StructurePiece piece = PieceFor(request.Tag, request.Style, request.Seed, request.Decay);
			if (piece == null)
			{
				metrics = default;
				return false;
			}
			metrics = new PointOfInterestPieceMetrics
			{
				Footprint = piece.Footprint,
				Module = piece.ModuleLength,
				Depth = piece.Depth,
				Height = piece.Height,
				Deck = piece.Anchor == StructureAnchor.Deck,
			};
			return true;
		}

		// ── Queries ──────────────────────────────────────────────────

		/// <summary>The piece a request lays: the seeded pick among <see cref="Candidates"/>, swapped for its ruin past <see cref="RuinDecay"/>.</summary>
		public static StructurePiece PieceFor(string query, string style, int seed, float decay)
		{
			List<StructurePiece> candidates = Candidates(query, style);
			if (candidates.Count == 0)
			{
				return null;
			}
			// The kit's own pick hash, so a plain tag picks what StructureKit.Pick would.
			StructurePiece piece = candidates[(int)(ProceduralNoise.Mix((uint)seed ^ 0x5bd1e995u) % (uint)candidates.Count)];
			if (decay >= RuinDecay && string.IsNullOrEmpty(piece.RuinOf))
			{
				StructurePiece ruin = RuinOf(piece.Id);
				if (ruin != null)
				{
					piece = ruin;
				}
			}
			return piece;
		}

		/// <summary>Whether the kit has any piece for a query (in any style).</summary>
		public static bool Resolves(string query) => Candidates(query, null).Count > 0;

		/// <summary>The kit's pieces a query names, in table order, narrowed to the style when any has it. Pure.</summary>
		public static List<StructurePiece> Candidates(string query, string style)
		{
			var list = new List<StructurePiece>();
			if (string.IsNullOrWhiteSpace(query))
			{
				return list;
			}
			query = query.Trim();
			if (query[0] == '=')
			{
				StructurePiece exact = StructureKit.Get(query.Substring(1).Trim());
				if (exact != null)
				{
					list.Add(exact);
				}
				return list;
			}
			Parse(query, out List<string> required, out List<string> excluded);
			if (required.Count == 0)
			{
				return list;
			}
			foreach (StructurePiece piece in StructureKit.All)
			{
				bool fits = true;
				foreach (string tag in required)
				{
					fits &= piece.HasTag(tag);
				}
				foreach (string tag in excluded)
				{
					fits &= !piece.HasTag(tag);
				}
				if (fits)
				{
					list.Add(piece);
				}
			}
			PreferWithout(list, "ruin", required);
			PreferWithout(list, "stilt", required);
			StructureStyle? wanted = ParseStyle(style);
			if (wanted != null)
			{
				bool any = false;
				foreach (StructurePiece piece in list)
				{
					any |= piece.Style == wanted.Value;
				}
				if (any)
				{
					list.RemoveAll(p => p.Style != wanted.Value);
				}
			}
			return list;
		}

		/// <summary>"timber" → Timber; null for anything that is not a style name (a race key, empty).</summary>
		public static StructureStyle? ParseStyle(string style)
		{
			if (string.IsNullOrWhiteSpace(style))
			{
				return null;
			}
			foreach (StructureStyle value in (StructureStyle[])Enum.GetValues(typeof(StructureStyle)))
			{
				if (string.Equals(value.ToString(), style.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					return value;
				}
			}
			return null;
		}

		/// <summary>Splits "a+b!c" into the tags it needs and the tags it refuses.</summary>
		public static void Parse(string query, out List<string> required, out List<string> excluded)
		{
			required = new List<string>();
			excluded = new List<string>();
			int start = 0;
			bool exclude = false;
			for (int i = 0; i <= query.Length; i++)
			{
				if (i < query.Length && query[i] != '+' && query[i] != '!')
				{
					continue;
				}
				string tag = query.Substring(start, i - start).Trim();
				if (tag.Length > 0)
				{
					(exclude ? excluded : required).Add(tag);
				}
				if (i < query.Length)
				{
					exclude = query[i] == '!';
				}
				start = i + 1;
			}
		}

		/// <summary>The kit's ruin made from a piece, or null.</summary>
		private static StructurePiece RuinOf(string id)
		{
			foreach (StructurePiece piece in StructureKit.All)
			{
				if (piece.RuinOf == id)
				{
					return piece;
				}
			}
			return null;
		}

		/// <summary>Drops the pieces carrying a tag the query did not ask for, unless that would leave none.</summary>
		private static void PreferWithout(List<StructurePiece> list, string tag, List<string> required)
		{
			if (required.Contains(tag))
			{
				return;
			}
			bool any = false;
			foreach (StructurePiece piece in list)
			{
				any |= !piece.HasTag(tag);
			}
			if (any)
			{
				list.RemoveAll(p => p.HasTag(tag));
			}
		}
	}
}
#endif
