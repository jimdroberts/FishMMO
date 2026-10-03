#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What is the generator's, and the small bookkeeping that remains once nothing generated is
	/// committed: the asset label, and file hashes for telling an unchanged rewrite from a real one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Everything generated is the generator's.</b> This class used to keep a committed JSON ledger
	/// of the wrappers' hashes, so that a wrapper Jim had replaced by hand under a generated name was
	/// kept rather than overwritten. Since 2026-10-02 nothing generated is committed: wrappers are build
	/// output, regenerated on every machine with path-derived GUIDs, so a hand edit to one would live
	/// on one machine only and vanish at the next regeneration anyway. The rule is now simply that
	/// every file under <see cref="ProceduralArtPayload.GeneratedRoots"/> is the generator's to rewrite
	/// (<see cref="IsGeneratorOwned"/>).
	/// </para>
	/// <para>
	/// <b>How art is replaced instead.</b> Through <c>Assets/LOCAL</c> — a <see cref="FishMMO.Shared.Biomes.BiomeLocalArt"/>
	/// sidecar, or a LOCAL texture/terrain layer of the same name — which the terrain arrays read; or
	/// by pointing a biome slot (<c>TerrainTextureLayer.terrainLayer</c>) or a scatter rule at a
	/// committed asset OUTSIDE the generated folders. The authoring pass treats any slot or rule that
	/// names a generated asset as procedural, never as authored, so it never mistakes build output for
	/// somebody's work.
	/// </para>
	/// <para>
	/// What remains of the record — the fingerprint, seed and file list of the last complete
	/// generation — is <see cref="ProceduralArtPayload.LedgerPath"/>, gitignored with the rest. The old
	/// committed ledger file (<see cref="LegacyPath"/>) is deleted when found.
	/// </para>
	/// </remarks>
	public static class ProceduralArtLedger
	{
		/// <summary>Applied to every generated asset, so they can be found in the Project window with <c>l:FishMMOProceduralArt</c>.</summary>
		public const string Label = "FishMMOProceduralArt";

		/// <summary>Where the committed wrapper ledger lived until 2026-10-02. Meaningless now; removed by the generator.</summary>
		public const string LegacyPath = ProceduralArtCatalogue.Root + "/ProceduralArtLedger.json";

		/// <summary>True for every generated file: payload, wrappers and built terrain layers. Always the generator's to rewrite.</summary>
		public static bool IsGeneratorOwned(string path) => ProceduralArtPayload.IsGenerated(path);

		/// <summary>Labels a written asset so it can be found in the Project window.</summary>
		/// <remarks>
		/// Reads the .meta first: the generator writes the label into every .meta it creates
		/// (<see cref="ProceduralArtPayload.LabelsText"/>), so this normally neither loads the asset
		/// nor calls <c>SetLabels</c>, each of which costs an asset-database round trip per file.
		/// </remarks>
		public static void Record(string path)
		{
			if (!File.Exists(path))
			{
				return;
			}
			string meta = path + ".meta";
			if (File.Exists(meta) && MetaHasLabel(File.ReadAllText(meta), Label))
			{
				return;
			}
			UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
			if (asset != null)
			{
				var labels = new List<string>(AssetDatabase.GetLabels(asset));
				if (!labels.Contains(Label))
				{
					labels.Add(Label);
					AssetDatabase.SetLabels(asset, labels.ToArray());
				}
			}
		}

		/// <summary>True when a .meta's top-level <c>labels:</c> list holds <paramref name="label"/>.</summary>
		public static bool MetaHasLabel(string metaText, string label)
		{
			if (string.IsNullOrEmpty(metaText))
			{
				return false;
			}
			bool inLabels = false;
			foreach (string raw in metaText.Replace("\r\n", "\n").Split('\n'))
			{
				if (raw.StartsWith("labels:", StringComparison.Ordinal))
				{
					inLabels = true;
					continue;
				}
				if (!inLabels)
				{
					continue;
				}
				if (!raw.StartsWith("- ", StringComparison.Ordinal))
				{
					return false;
				}
				if (string.Equals(raw.Substring(2).Trim(), label, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Deletes the committed-era wrapper ledger if it is still on disk.</summary>
		public static void DeleteLegacy()
		{
			if (File.Exists(LegacyPath))
			{
				ProceduralArtPayload.Delete(LegacyPath);
			}
		}

		public static string HashFile(string path)
		{
			using (var sha = SHA1.Create())
			using (FileStream stream = File.OpenRead(path))
			{
				return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
			}
		}
	}
}
#endif
