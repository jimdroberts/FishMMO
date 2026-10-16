#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Lays a structure kit piece in a biome's structure palette at the cut (2026-10-16, I6a): the materials its prefab is drawn
	/// in, as a baked prop's (ScenePropSet.Prototype.Materials, through <see cref="PointOfInterestPropSink"/>) or on a scene
	/// object's renderers (<see cref="Dress"/>: doors, moored and sailing ships).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The kit's prefab, the palette's materials.</b> A piece's prefab wears the kit's material on every submesh, a submesh
	/// per material and palette part (StructurePalettes); the palette's material for each is named by its key and made by the
	/// art generator. Nothing is made here, and where every submesh comes out in the kit's own material (the kit's palette,
	/// every biome's today) the answer is null: the prop is the kit's prefab exactly as before.
	/// </para>
	/// <para>
	/// <b>What it leaves alone.</b> Anything that is not a generated kit prefab at its own path (a LOCAL or hand-made stand-in,
	/// a rock, a plant), and a kit prefab whose renderers do not wear what the kit's builders now give it (art older than its
	/// code: said once, laid as it is until the art is generated). A material a palette names but the art does not have yet is
	/// laid in the kit's material for that submesh and said once.
	/// </para>
	/// </remarks>
	public static class StructurePaletteDressing
	{
		private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
		private static readonly HashSet<string> noted = new HashSet<string>(StringComparer.Ordinal);
		private static readonly object gate = new object();

		/// <summary>
		/// The materials a kit prefab is drawn in under <paramref name="palette"/>, every LOD renderer's slots level by level;
		/// null when it is drawn in its own (no palette, the palette lays it as the kit does, or it is no kit piece).
		/// <paramref name="host"/>: the style of the building the piece belongs to when not its own (a door's).
		/// </summary>
		public static Material[] MaterialsFor(GameObject prefab, BiomeStructurePalette palette, StructureStyle? host = null)
		{
			if (prefab == null || palette == null || !KitPiece(prefab, out StructurePiece piece, out int variant, out StructureFinish finish))
			{
				return null;
			}
			string[][] names = StructurePalettes.MaterialNames(piece, variant, finish, palette, host, out bool differs);
			if (!differs)
			{
				return null;
			}
			List<Renderer[]> levels = Levels(prefab);
			string[][] kit = StructurePalettes.MaterialNames(piece, variant, finish, null, null, out _);
			if (!Wears(levels, kit))
			{
				Note($"stale:{piece.Id}", $"[Structure palette] {prefab.name} does not wear the materials its piece's builder now gives it (the structure art is older than its code): "
					+ "every such piece is laid in the kit's materials until Biome Tools → Generate biome art has run. Logged once a session for each piece.");
				return null;
			}
			NoteRoofs(piece, palette, host);
			var dress = new List<Material>();
			for (int lod = 0; lod < levels.Count; lod++)
			{
				foreach (Renderer renderer in levels[lod])
				{
					if (renderer == null)
					{
						continue;
					}
					Material[] own = renderer.sharedMaterials;
					for (int i = 0; i < own.Length; i++)
					{
						Material wanted = i < names[lod].Length ? Load(names[lod][i]) : null;
						if (wanted == null && i < names[lod].Length)
						{
							Note($"missing:{names[lod][i]}", $"[Structure palette] {names[lod][i]} is not generated yet (a palette names it): {piece.Id} and every piece that wears it are laid in the kit's material "
								+ "there until Biome Tools → Generate biome art has run. Logged once a session for each material.");
						}
						dress.Add(wanted != null ? wanted : own[i]);
					}
				}
			}
			return dress.ToArray();
		}

		/// <summary>
		/// Lays a scene object made from a kit prefab (<paramref name="instance"/>, from <paramref name="prefab"/>) in a palette:
		/// its renderers wear <see cref="MaterialsFor"/>. False (and the object untouched) when the palette lays it as the kit does.
		/// </summary>
		public static bool Dress(GameObject instance, GameObject prefab, BiomeStructurePalette palette, StructureStyle? host = null)
		{
			Material[] dress = instance != null ? MaterialsFor(prefab, palette, host) : null;
			if (dress == null)
			{
				return false;
			}
			List<Renderer[]> levels = Levels(instance);
			int slot = 0;
			foreach (Renderer[] level in levels)
			{
				foreach (Renderer renderer in level)
				{
					if (renderer != null)
					{
						slot += renderer.sharedMaterials.Length;
					}
				}
			}
			if (slot != dress.Length)
			{
				return false;
			}
			slot = 0;
			foreach (Renderer[] level in levels)
			{
				foreach (Renderer renderer in level)
				{
					if (renderer == null)
					{
						continue;
					}
					var worn = new Material[renderer.sharedMaterials.Length];
					Array.Copy(dress, slot, worn, 0, worn.Length);
					slot += worn.Length;
					renderer.sharedMaterials = worn;
				}
			}
			return true;
		}

		/// <summary>The style of a kit piece by id (a door's building), or null.</summary>
		public static StructureStyle? StyleOf(string pieceId)
		{
			StructurePiece piece = string.IsNullOrEmpty(pieceId) ? null : StructurePieces.Find(pieceId);
			return piece != null ? piece.Style : (StructureStyle?)null;
		}

		/// <summary>Whether a prefab is a generated kit piece at its own path, and which piece, variant and finish.</summary>
		private static bool KitPiece(GameObject prefab, out StructurePiece piece, out int variant, out StructureFinish finish)
		{
			finish = StructureFinish.None;
			if (!StructurePieces.TryParsePrefab(prefab.name, out piece, out variant, out finish))
			{
				return false;
			}
			string path = AssetDatabase.GetAssetPath(prefab);
			return string.Equals(path, StructureKit.PrefabPath(piece.Id, variant, finish), StringComparison.Ordinal);
		}

		/// <summary>A prefab's renderers by level, as the instanced renderer reads them: its LOD group's, or every mesh renderer as one level.</summary>
		private static List<Renderer[]> Levels(GameObject root)
		{
			var levels = new List<Renderer[]>();
			LODGroup group = root.GetComponent<LODGroup>();
			if (group != null)
			{
				foreach (LOD lod in group.GetLODs())
				{
					levels.Add(lod.renderers ?? Array.Empty<Renderer>());
				}
			}
			else
			{
				levels.Add(root.GetComponentsInChildren<MeshRenderer>(true));
			}
			return levels;
		}

		/// <summary>Whether every level's renderers wear, slot for slot, the kit's material names for the piece.</summary>
		private static bool Wears(List<Renderer[]> levels, string[][] kit)
		{
			if (levels.Count != kit.Length)
			{
				return false;
			}
			for (int lod = 0; lod < levels.Count; lod++)
			{
				int at = 0;
				foreach (Renderer renderer in levels[lod])
				{
					if (renderer == null)
					{
						continue;
					}
					foreach (Material m in renderer.sharedMaterials)
					{
						if (at >= kit[lod].Length || m == null || !string.Equals(m.name, kit[lod][at], StringComparison.Ordinal))
						{
							return false;
						}
						at++;
					}
				}
				if (at != kit[lod].Length)
				{
					return false;
				}
			}
			return true;
		}

		// A roof laid in a material that wants it steeper than the kit builds that roof: said once for each style, role and material.
		private static void NoteRoofs(StructurePiece piece, BiomeStructurePalette palette, StructureStyle? host)
		{
			foreach (BiomeStructureRole role in new[] { BiomeStructureRole.Roof, BiomeStructureRole.ShingleRoof })
			{
				var style = (BiomeStructureStyle)(host ?? piece.Style);
				BiomeStructureMaterial material = palette.For(style, role).Material;
				if (!StructurePalettes.RoofFits(style, role, material))
				{
					Note($"roof:{style}/{role}/{material}", $"[Structure palette] A palette roofs {style} buildings' {role} in {material}, which wants a steeper pitch than the kit builds that roof "
						+ $"({StructurePalettes.BuiltPitch(style, role):0}°); it is laid at the kit's pitch. Logged once a session.");
				}
			}
		}

		private static Material Load(string name)
		{
			lock (gate)
			{
				if (!materials.TryGetValue(name, out Material m) || m == null)
				{
					m = AssetDatabase.LoadAssetAtPath<Material>(ProceduralArtCatalogue.MaterialPath(name));
					materials[name] = m;
				}
				return m;
			}
		}

		private static void Note(string key, string message)
		{
			lock (gate)
			{
				if (!noted.Add(key))
				{
					return;
				}
			}
			Debug.LogWarning(message);
		}
	}
}
#endif
