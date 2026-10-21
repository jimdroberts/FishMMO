#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>A family of generated art: the steps of the generation that make it, and the sources that decide its bytes.</summary>
	/// <remarks>Append-only: names are recorded in the payload ledger.</remarks>
	public enum ArtFamily
	{
		/// <summary>The ground surfaces, their terrain layers and the built layers.</summary>
		Ground,
		/// <summary>The bark surfaces.</summary>
		Bark,
		/// <summary>The foliage atlas, the detail plants and the sea floor's.</summary>
		Plants,
		/// <summary>Rock and ice surfaces and materials, legacy boulders, rock and crystal formations, ice.</summary>
		Rocks,
		/// <summary>Cliff rocks and cliff sections.</summary>
		Cliffs,
		Bushes,
		Trees,
		/// <summary>The vine atlas, the vines and the trunk climbers.</summary>
		Vines,
		Deadwood,
		/// <summary>The structure kit: its surfaces, materials and pieces, and the cave kit's glow art built with them.</summary>
		Structures,
	}

	/// <summary>
	/// The generation split by family, so a change to one family's code rebuilds that family and what is made from it,
	/// not the whole art set. A full run of everything took 7–13 minutes, almost all of it rebuilding art that came out
	/// byte for byte the same (4,257 files "regenerated unchanged" against 5 written).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Fingerprints.</b> Each family's fingerprint hashes the format, the seed and sizes, the SHARED sources (anything
	/// every family is made with: the driver, the mesh builder, noise, the surface synthesiser, the catalogue), its own
	/// sources, and the fingerprints of the families it is made from (<see cref="InputsOf"/>): a change to bark rebuilds
	/// the trees, bushes and deadwood that wear it.
	/// </para>
	/// <para>
	/// <b>Safe by default.</b> A hashed source that no rule names counts as shared, so it rebuilds everything; a step whose
	/// label no rule names is treated as shared too (it runs whenever anything is rebuilt). Only a rule naming a file or
	/// a step narrows what rebuilds, and the tests hold every generator source to one family or the shared set.
	/// </para>
	/// </remarks>
	public static class ProceduralArtFamilies
	{
		public static readonly ArtFamily[] All = (ArtFamily[])Enum.GetValues(typeof(ArtFamily));

		/// <summary>
		/// Which family each non-shared source belongs to: a file name, or a prefix ending in '*' (so a new
		/// StructurePieces.*.cs is the structures' from its first line). A file in no rule is shared.
		/// </summary>
		private static readonly (string pattern, ArtFamily[] families)[] FileRules =
		{
			("FoliageAtlas.cs", new[] { ArtFamily.Plants }),
			("VegetationMeshes.cs", new[] { ArtFamily.Plants }),
			("PlantParts.cs", new[] { ArtFamily.Plants, ArtFamily.Bushes, ArtFamily.Trees }),
			("FloraMeshes.cs", new[] { ArtFamily.Plants }),
			("SeaFloorMeshes.cs", new[] { ArtFamily.Plants }),
			("ProceduralArtCatalogue.Flora.cs", new[] { ArtFamily.Plants, ArtFamily.Deadwood }),
			("ProceduralArtCatalogue.Sea.cs", new[] { ArtFamily.Plants }),
			// The plants floating on still water (2026-10-18): details like the rest.
			("FloatingPlantMeshes.cs", new[] { ArtFamily.Plants }),
			("ProceduralArtCatalogue.Floating.cs", new[] { ArtFamily.Plants }),
			("BiomeArtGenerator.Rocks.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs }),
			("ProceduralArtCatalogue.Rocks.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs }),
			("RockMeshes.cs", new[] { ArtFamily.Rocks }),
			("StoneMeshes.cs", new[] { ArtFamily.Rocks }),
			("StoneStyles.cs", new[] { ArtFamily.Rocks }),
			("RockFormations.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs }),
			("RockTypes.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs }),
			("RockArtNames.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs }),
			("CrystalFormations.cs", new[] { ArtFamily.Rocks }),
			("IceMeshes.cs", new[] { ArtFamily.Rocks }),
			("IceSurfaces.cs", new[] { ArtFamily.Rocks }),
			("MeshDecimator.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs }),
			("ProceduralSurfaceNets.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs }),
			("CliffSections.cs", new[] { ArtFamily.Cliffs }),
			("CliffRocks.cs", new[] { ArtFamily.Cliffs }),
			("MossAmounts.cs", new[] { ArtFamily.Rocks, ArtFamily.Cliffs, ArtFamily.Deadwood, ArtFamily.Structures }),
			("BiomeArtGenerator.Bushes.cs", new[] { ArtFamily.Bushes }),
			("BushMeshes.cs", new[] { ArtFamily.Bushes }),
			("ProceduralArtCatalogue.Bushes.cs", new[] { ArtFamily.Bushes }),
			("TreeMeshes.cs", new[] { ArtFamily.Trees }),
			("BillboardImpostor.cs", new[] { ArtFamily.Trees, ArtFamily.Bushes }),
			("VineAtlas.cs", new[] { ArtFamily.Vines }),
			("VineMeshes.cs", new[] { ArtFamily.Vines }),
			("ProceduralArtCatalogue.Vines.cs", new[] { ArtFamily.Vines }),
			("BiomeArtGenerator.Vines.cs", new[] { ArtFamily.Vines }),
			("DeadwoodMeshes.cs", new[] { ArtFamily.Deadwood }),
			("BiomeArtGenerator.Deadwood.cs", new[] { ArtFamily.Deadwood }),
			("StructurePieces*", new[] { ArtFamily.Structures }),
			("StructureSolids.cs", new[] { ArtFamily.Structures }),
			("StructureSurfaces.cs", new[] { ArtFamily.Structures }),
			// The structure palette's parts, keys and surfaces (2026-10-16).
			("StructureSurfaces.Palette.cs", new[] { ArtFamily.Structures }),
			("StructurePalettes.cs", new[] { ArtFamily.Structures }),
			// The house roofs built at their palette's pitch (2026-10-18).
			("StructureRoofs.cs", new[] { ArtFamily.Structures }),
			// Sources outside the generator's folder whose values the structures bake (ProceduralArtPayload.ForeignSources, 2026-10-18).
			("SceneGlow.cs", new[] { ArtFamily.Structures }),
			("BiomeStructurePalette.cs", new[] { ArtFamily.Structures }),
			("StructureRuins.cs", new[] { ArtFamily.Structures }),
			("StructureKit.cs", new[] { ArtFamily.Structures }),
			("ProceduralArtCatalogue.Structures.cs", new[] { ArtFamily.Structures }),
			("BiomeArtGenerator.Structures.cs", new[] { ArtFamily.Structures }),
			// The cave kit's glow art, built in the structures' steps (2026-10-13).
			("CaveGlowMeshes.cs", new[] { ArtFamily.Structures }),
			("ProceduralArtCatalogue.Cave.cs", new[] { ArtFamily.Structures }),
			("BiomeArtGenerator.CaveGlow.cs", new[] { ArtFamily.Structures }),
			// What hangs from a cave's roof and piles under it, built as the glow art (2026-10-20, caves livened).
			("CaveGlowMeshes.Hanging.cs", new[] { ArtFamily.Structures }),
			// An ice cave's ice, built as the glow art (2026-10-20, caves livened).
			("CaveGlowMeshes.Ice.cs", new[] { ArtFamily.Structures }),
			// The run-time cave cover's mesh, built in the structures' steps (2026-10-20), and the type table outside the folder
			// whose order is its submeshes'.
			("CaveCoverMeshes.cs", new[] { ArtFamily.Structures }),
			("BiomeArtGenerator.CaveCover.cs", new[] { ArtFamily.Structures }),
			("CaveCoverTypes.cs", new[] { ArtFamily.Structures }),
		};

		/// <summary>
		/// The families each family is made from, whose change rebuilds it: trees and bushes wear the foliage atlas and the
		/// barks; climbers are built on the trees' trunks; deadwood wears the barks and the rock surfaces; cliffs the rock
		/// materials.
		/// </summary>
		public static ArtFamily[] InputsOf(ArtFamily family)
		{
			switch (family)
			{
				case ArtFamily.Bushes: return new[] { ArtFamily.Plants, ArtFamily.Bark };
				case ArtFamily.Trees: return new[] { ArtFamily.Plants, ArtFamily.Bark };
				case ArtFamily.Vines: return new[] { ArtFamily.Trees };
				case ArtFamily.Deadwood: return new[] { ArtFamily.Bark, ArtFamily.Rocks };
				case ArtFamily.Cliffs: return new[] { ArtFamily.Rocks };
				default: return Array.Empty<ArtFamily>();
			}
		}

		/// <summary>The families a hashed source belongs to; empty for a shared one (every family's).</summary>
		public static ArtFamily[] FamiliesOfFile(string fileName)
		{
			foreach ((string pattern, ArtFamily[] families) in FileRules)
			{
				bool match = pattern.EndsWith("*", StringComparison.Ordinal)
					? fileName.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal)
					: string.Equals(fileName, pattern, StringComparison.Ordinal);
				if (match)
				{
					return families;
				}
			}
			return Array.Empty<ArtFamily>();
		}

		/// <summary>
		/// The families a generation step makes, by its label (the group before any ':'); empty for a shared step, which
		/// runs whenever anything is rebuilt (an unknown label is shared, so nothing new is ever skipped by mistake).
		/// </summary>
		public static ArtFamily[] FamiliesOfStep(string label)
		{
			if (string.IsNullOrEmpty(label))
			{
				return Array.Empty<ArtFamily>();
			}
			int colon = label.IndexOf(':');
			string group = colon > 0 ? label.Substring(0, colon) : label;
			switch (group)
			{
				case "Ground":
				case "Terrain layers":
				case "Built terrain layers":
					return new[] { ArtFamily.Ground };
				case "Bark":
					return new[] { ArtFamily.Bark };
				// The foliage atlas step writes the vines' atlas too.
				case "Foliage atlas":
					return new[] { ArtFamily.Plants, ArtFamily.Vines };
				case "Detail plants":
					return new[] { ArtFamily.Plants };
				case "Rock and ice surfaces":
				case "Rocks":
				case "Rock formations":
				case "Ice":
					return new[] { ArtFamily.Rocks };
				case "Rock, ice and cliff materials":
					return new[] { ArtFamily.Rocks, ArtFamily.Cliffs };
				case "Cliff rocks":
				case "Cliff sections":
					return new[] { ArtFamily.Cliffs };
				case "Bush":
					return new[] { ArtFamily.Bushes };
				case "Tree":
					return new[] { ArtFamily.Trees };
				case "Vine":
				case "Vine climbers":
					return new[] { ArtFamily.Vines };
				case "Deadwood":
					return new[] { ArtFamily.Deadwood };
				case "Structure surfaces":
				case "Structure materials":
				case "Structure":
					return new[] { ArtFamily.Structures };
				default:
					return Array.Empty<ArtFamily>();
			}
		}

		/// <summary>Whether a step is to rebuild what it makes: always when everything is (<paramref name="dirty"/> null).</summary>
		public static bool StepDirty(string label, ICollection<ArtFamily> dirty)
		{
			if (dirty == null)
			{
				return true;
			}
			ArtFamily[] families = FamiliesOfStep(label);
			if (families.Length == 0)
			{
				return dirty.Count > 0;
			}
			foreach (ArtFamily family in families)
			{
				if (dirty.Contains(family))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Every family's fingerprint from the sources' text (<paramref name="read"/>: a source's text, or null when it is
		/// missing). <paramref name="header"/> carries the format, seed and sizes every family is made at.
		/// </summary>
		public static Dictionary<ArtFamily, string> Fingerprints(string header, IEnumerable<string> sources, Func<string, string> read)
		{
			var shared = new StringBuilder(header).Append('\n');
			var own = new Dictionary<ArtFamily, StringBuilder>();
			foreach (ArtFamily family in All)
			{
				own[family] = new StringBuilder();
			}
			foreach (string file in sources)
			{
				string text = read(file);
				string entry = file + "\n" + (text != null ? text.Replace("\r\n", "\n") : "<missing>") + "\n";
				ArtFamily[] families = FamiliesOfFile(file);
				if (families.Length == 0)
				{
					shared.Append(entry);
					continue;
				}
				foreach (ArtFamily family in families)
				{
					own[family].Append(entry);
				}
			}
			var prints = new Dictionary<ArtFamily, string>();
			string sharedHash = Hash(shared.ToString());
			// In dependency order: a family's inputs are hashed before it (resolved recursively, never in a cycle).
			string Of(ArtFamily family, int depth)
			{
				if (prints.TryGetValue(family, out string done))
				{
					return done;
				}
				if (depth > All.Length)
				{
					throw new InvalidOperationException($"the art families' inputs form a cycle at {family}");
				}
				var sb = new StringBuilder(sharedHash).Append('|').Append(family).Append('|').Append(own[family]);
				foreach (ArtFamily input in InputsOf(family))
				{
					sb.Append("|input ").Append(input).Append('=').Append(Of(input, depth + 1));
				}
				string print = Hash(sb.ToString());
				prints[family] = print;
				return print;
			}
			foreach (ArtFamily family in All)
			{
				Of(family, 0);
			}
			return prints;
		}

		/// <summary>One fingerprint over every family's: the art as a whole.</summary>
		public static string Combined(Dictionary<ArtFamily, string> prints)
		{
			var sb = new StringBuilder();
			foreach (ArtFamily family in All)
			{
				sb.Append(family).Append('=').Append(prints.TryGetValue(family, out string p) ? p : "?").Append('\n');
			}
			return Hash(sb.ToString());
		}

		/// <summary>
		/// The families whose fingerprint differs from the record (none recorded: all of them). Pure, for the tests.
		/// </summary>
		public static HashSet<ArtFamily> Dirty(Dictionary<ArtFamily, string> current, IDictionary<string, string> recorded)
		{
			var dirty = new HashSet<ArtFamily>();
			foreach (ArtFamily family in All)
			{
				if (recorded == null || !recorded.TryGetValue(family.ToString(), out string was)
					|| !current.TryGetValue(family, out string now) || !string.Equals(was, now, StringComparison.Ordinal))
				{
					dirty.Add(family);
				}
			}
			return dirty;
		}

		private static string Hash(string text)
		{
			using (SHA1 sha = SHA1.Create())
			{
				byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
				return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
			}
		}
	}
}
#endif
