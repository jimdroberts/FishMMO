#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The structure kit as the POI templates see it: pieces by tag and style, a seeded pick, and the generated prefab for a
	/// piece, variant and finish (Jim, 2026-10-10: generated points of interest are built from a procedural kit, any piece
	/// replaceable by a LOCAL or hand-made prefab).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Conventions</b> (<see cref="StructurePieces"/>): origin at the footprint's centre on the ground line (deck level
	/// for <see cref="StructureAnchor.Deck"/>), front down −z, modular pieces along x (piers and bridge spans along z)
	/// exactly <see cref="StructurePiece.ModuleLength"/> long. <see cref="StructurePiece.Footprint"/> and
	/// <see cref="StructurePiece.Height"/> are measured from the meshes.
	/// </para>
	/// <para>
	/// <b>Replacing a piece.</b> A prefab named like the generated one (<c>Structure_&lt;Id&gt;_&lt;v&gt;[_&lt;Finish&gt;]</c>)
	/// in <see cref="BiomeLocalArt.PrefabsFolder"/> stands in for it when the caller allows LOCAL art — which, by the LOCAL
	/// rule (LocalArtScope), only a scene stored under Assets/LOCAL may do.
	/// </para>
	/// </remarks>
	public static class StructureKit
	{
		/// <summary>Every piece, in table order.</summary>
		public static IReadOnlyList<StructurePiece> All => StructurePieces.All;

		/// <summary>Every tag in use.</summary>
		public static IReadOnlyList<string> Tags => StructurePieces.Tags;

		/// <summary>The piece with this id, or null.</summary>
		public static StructurePiece Get(string pieceId) => StructurePieces.Find(pieceId);

		/// <summary>Every piece carrying the tag, in table order.</summary>
		public static IEnumerable<StructurePiece> WithTag(string tag)
		{
			foreach (StructurePiece p in StructurePieces.All)
			{
				if (p.HasTag(tag)) yield return p;
			}
		}

		/// <summary>Every piece carrying all the tags, in table order.</summary>
		public static IEnumerable<StructurePiece> WithTags(params string[] tags)
		{
			foreach (StructurePiece p in StructurePieces.All)
			{
				bool all = true;
				foreach (string t in tags)
				{
					all &= p.HasTag(t);
				}
				if (all) yield return p;
			}
		}

		/// <summary>The pieces with the tag in the style (any style when <paramref name="style"/> is null), in table order.</summary>
		public static List<StructurePiece> Matching(string tag, StructureStyle? style = null)
		{
			var list = new List<StructurePiece>();
			foreach (StructurePiece p in WithTag(tag))
			{
				if (style == null || p.Style == style.Value) list.Add(p);
			}
			return list;
		}

		/// <summary>
		/// A seeded pick among the pieces with the tag, in the style when any has it (else among them all); null when no
		/// piece has the tag. The same seed always picks the same piece while the table is unchanged.
		/// </summary>
		public static StructurePiece Pick(string tag, StructureStyle? style, int seed)
		{
			List<StructurePiece> list = Matching(tag, style);
			if (list.Count == 0 && style != null)
			{
				list = Matching(tag);
			}
			return list.Count == 0 ? null : list[(int)(ProceduralNoise.Mix((uint)seed ^ 0x5bd1e995u) % (uint)list.Count)];
		}

		/// <summary>A seeded variant of a piece.</summary>
		public static int PickVariant(StructurePiece piece, int seed)
		{
			return piece == null ? 0 : (int)(ProceduralNoise.Mix((uint)seed ^ 0x27d4eb2fu) % (uint)Mathf.Max(1, piece.Variants));
		}

		/// <summary>The generated prefab's project path.</summary>
		public static string PrefabPath(string pieceId, int variant, StructureFinish finish = StructureFinish.None)
		{
			return ProceduralArtCatalogue.PrefabPath(StructurePieces.PrefabName(pieceId, variant, finish));
		}

		/// <summary>The LOCAL stand-in's path for a piece (it may not exist).</summary>
		public static string LocalPrefabPath(string pieceId, int variant, StructureFinish finish = StructureFinish.None)
		{
			string name = StructurePieces.PrefabName(pieceId, variant, finish);
			return $"{BiomeLocalArt.PrefabsFolder}/{name.Substring(name.LastIndexOf('/') + 1)}.prefab";
		}

		/// <summary>
		/// The prefab for a piece's variant in a finish: the LOCAL stand-in when <paramref name="allowLocal"/> and one
		/// exists, else the generated one; null when the art has not been generated (run the art generator).
		/// </summary>
		public static GameObject Prefab(string pieceId, int variant, StructureFinish finish = StructureFinish.None, bool allowLocal = false)
		{
			StructurePiece piece = Get(pieceId);
			if (piece == null)
			{
				throw new ArgumentException($"no structure piece '{pieceId}'", nameof(pieceId));
			}
			variant = Mathf.Clamp(variant, 0, piece.Variants - 1);
			if (allowLocal)
			{
				var local = AssetDatabase.LoadAssetAtPath<GameObject>(LocalPrefabPath(pieceId, variant, finish));
				if (local != null) return local;
			}
			return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(pieceId, variant, finish));
		}
	}

	/// <summary>Generates the structure kit alone: from the dashboard, or a batch run's <c>-executeMethod</c>.</summary>
	public static class StructureKitGenerator
	{
		[DashboardTool(DashboardToolAttribute.Biomes, "Generate structure kit", Section = "Art", Order = 3,
			Tooltip = "Writes only the structure kit for points of interest (surfaces, materials, meshes, prefabs under Generated/Prefabs/Structures). The full biome art generation includes it.")]
		public static void GenerateFromDashboard()
		{
			Log(BiomeArtGenerator.GenerateStructuresOnly(ProceduralArtMode.Full, ProceduralArtCatalogue.DefaultSeed));
		}

		/// <summary>
		/// <c>-executeMethod FishMMO.Shared.WorldDesign.StructureKitGenerator.GenerateFromCommandLine</c>: generates the kit
		/// and, in batch mode, exits 0 when it completed without problems, 1 otherwise.
		/// </summary>
		public static void GenerateFromCommandLine()
		{
			int code = 1;
			try
			{
				BiomeArtGenerator.Report report = BiomeArtGenerator.GenerateStructuresOnly(ProceduralArtMode.Full, ProceduralArtCatalogue.DefaultSeed);
				Log(report);
				code = report.Completed && report.Problems.Count == 0 ? 0 : 1;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(code);
			}
		}

		private static void Log(BiomeArtGenerator.Report report)
		{
			if (report.Completed && report.Problems.Count == 0)
			{
				Debug.Log("[Structure kit] " + report);
			}
			else
			{
				Debug.LogWarning("[Structure kit] " + report);
			}
		}
	}
}
#endif
