#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Which texture slots a generated material or terrain layer must have filled, checked on the
	/// fresh object before it is written and on every file on disk after a generation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why it exists (2026-10-02).</b> Every generated material and terrain layer had been written
	/// with empty texture slots — distant trees drew as grey slabs (an untextured billboard cross with
	/// the shader's white default, alpha 1, under alpha clip) and the ground drew flat — and the
	/// generator reported 34 problems among 1830 writes but still wrote every one of them, and a
	/// re-run rewrote the same empty files. The cause was upstream (the payload PNGs imported as
	/// cube maps, so <c>LoadAssetAtPath&lt;Texture2D&gt;</c> answered null; see
	/// <see cref="ProceduralArtTextureImport"/>), but a wrapper with an empty required slot is never
	/// right, whatever the cause: it is skipped (the previous file kept), reported, and the art left
	/// stale so the next run tries again. This class is the one definition of "required" both the
	/// writer and the after-run scan use.
	/// </para>
	/// <para>
	/// <b>The rule.</b> A material needs <c>_BaseMap</c> (every generated material is textured), and
	/// each map its own keywords say it samples: <c>_NORMALMAP</c> → <c>_BumpMap</c>,
	/// <c>_METALLICSPECGLOSSMAP</c> → <c>_MetallicGlossMap</c>, <c>_OCCLUSIONMAP</c> →
	/// <c>_OcclusionMap</c>. A material that turns a map's keyword on and leaves the map empty samples
	/// the shader's default, which is exactly the silent wrongness this guards against. A generated
	/// terrain layer needs all three of its maps (the generator always writes albedo, normal and mask).
	/// </para>
	/// </remarks>
	public static class ProceduralArtWrapperCheck
	{
		/// <summary>Slots every generated material must fill.</summary>
		public static readonly string[] AlwaysRequired = { "_BaseMap" };

		/// <summary>A keyword, and the slot that keyword makes the shader sample.</summary>
		public static readonly (string Keyword, string Slot)[] KeywordSlots =
		{
			("_NORMALMAP", "_BumpMap"),
			("_METALLICSPECGLOSSMAP", "_MetallicGlossMap"),
			("_OCCLUSIONMAP", "_OcclusionMap"),
		};

		/// <summary>The serialized fields of a generated terrain layer that must name a texture.</summary>
		public static readonly string[] TerrainLayerSlots = { "m_DiffuseTexture", "m_NormalMapTexture", "m_MaskMapTexture" };

		/// <summary>The slots a material with these keywords must fill, in a fixed order.</summary>
		public static List<string> RequiredMaterialSlots(ICollection<string> keywords)
		{
			var slots = new List<string>(AlwaysRequired);
			foreach ((string keyword, string slot) in KeywordSlots)
			{
				if (keywords != null && keywords.Contains(keyword))
				{
					slots.Add(slot);
				}
			}
			return slots;
		}

		// ── In memory: the fresh object, before it is written ────────

		/// <summary>The required slots of a fresh material that are empty; empty when it may be written.</summary>
		public static List<string> MissingInMaterial(Material material)
		{
			var missing = new List<string>();
			if (material == null)
			{
				missing.Add("(no material)");
				return missing;
			}
			var keywords = new HashSet<string>(material.shaderKeywords ?? Array.Empty<string>(), StringComparer.Ordinal);
			foreach (string slot in RequiredMaterialSlots(keywords))
			{
				if (material.HasTexture(slot) && material.GetTexture(slot) == null)
				{
					missing.Add(slot);
				}
			}
			return missing;
		}

		/// <summary>The maps of a fresh terrain layer that are empty; empty when it may be written.</summary>
		public static List<string> MissingInTerrainLayer(TerrainLayer layer)
		{
			var missing = new List<string>();
			if (layer == null)
			{
				missing.Add("(no terrain layer)");
				return missing;
			}
			if (layer.diffuseTexture == null) missing.Add(TerrainLayerSlots[0]);
			if (layer.normalMapTexture == null) missing.Add(TerrainLayerSlots[1]);
			if (layer.maskMapTexture == null) missing.Add(TerrainLayerSlots[2]);
			return missing;
		}

		// ── On disk: the serialized YAML (pure) ──────────────────────

		private static readonly Regex TexEnvName = new Regex(@"^\s*-\s+([A-Za-z0-9_]+):\s*$", RegexOptions.CultureInvariant);
		private static readonly Regex TextureRef = new Regex(@"^\s*m_Texture:\s*(\{.*\})\s*$", RegexOptions.CultureInvariant);
		private static readonly Regex ListItem = new Regex(@"^\s*-\s+(\S+)\s*$", RegexOptions.CultureInvariant);
		private static readonly Regex TopKey = new Regex(@"^  ([A-Za-z_][A-Za-z0-9_]*):(.*)$", RegexOptions.CultureInvariant);

		/// <summary>True for a serialized object reference that names nothing (<c>{fileID: 0}</c>).</summary>
		public static bool IsNullReference(string reference)
		{
			if (string.IsNullOrWhiteSpace(reference))
			{
				return true;
			}
			string r = reference.Replace(" ", string.Empty);
			return r == "{fileID:0}" || r.StartsWith("{fileID:0,", StringComparison.Ordinal) || r.StartsWith("{fileID:0}", StringComparison.Ordinal);
		}

		/// <summary>
		/// The required slots a serialized material (text YAML, as Unity writes a .mat) leaves empty.
		/// Reads <c>m_ValidKeywords</c> (or the older <c>m_ShaderKeywords</c> line) and the
		/// <c>m_TexEnvs</c> entries; a required slot that is absent from the file counts as empty.
		/// </summary>
		public static List<string> MissingInMaterialYaml(string yaml)
		{
			var keywords = new HashSet<string>(StringComparer.Ordinal);
			var textures = new Dictionary<string, bool>(StringComparer.Ordinal); // slot → filled
			string[] lines = (yaml ?? string.Empty).Replace("\r\n", "\n").Split('\n');
			string section = null;
			string slot = null;
			foreach (string line in lines)
			{
				Match top = TopKey.Match(line);
				if (top.Success)
				{
					section = top.Groups[1].Value;
					slot = null;
					if (section == "m_ShaderKeywords")
					{
						foreach (string k in top.Groups[2].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
						{
							keywords.Add(k);
						}
					}
					continue;
				}
				if (section == "m_ValidKeywords")
				{
					Match item = ListItem.Match(line);
					if (item.Success)
					{
						keywords.Add(item.Groups[1].Value);
					}
					continue;
				}
				if (section == "m_SavedProperties")
				{
					Match name = TexEnvName.Match(line);
					if (name.Success)
					{
						slot = name.Groups[1].Value;
						continue;
					}
					Match tex = TextureRef.Match(line);
					if (tex.Success && slot != null)
					{
						textures[slot] = !IsNullReference(tex.Groups[1].Value);
						slot = null;
					}
				}
			}
			var missing = new List<string>();
			foreach (string required in RequiredMaterialSlots(keywords))
			{
				if (!textures.TryGetValue(required, out bool filled) || !filled)
				{
					missing.Add(required);
				}
			}
			return missing;
		}

		/// <summary>The maps a serialized terrain layer (.terrainlayer text YAML) leaves empty.</summary>
		public static List<string> MissingInTerrainLayerYaml(string yaml)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (string line in (yaml ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
			{
				Match top = TopKey.Match(line);
				if (top.Success)
				{
					values[top.Groups[1].Value] = top.Groups[2].Value.Trim();
				}
			}
			var missing = new List<string>();
			foreach (string slot in TerrainLayerSlots)
			{
				if (!values.TryGetValue(slot, out string reference) || IsNullReference(reference))
				{
					missing.Add(slot);
				}
			}
			return missing;
		}

		/// <summary>The empty required slots of one generated wrapper's text, by its extension; empty for any other file.</summary>
		public static List<string> MissingSlots(string path, string text)
		{
			string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
			if (ext == ".mat")
			{
				return MissingInMaterialYaml(text);
			}
			if (ext == ".terrainlayer")
			{
				return MissingInTerrainLayerYaml(text);
			}
			return new List<string>();
		}

		/// <summary>
		/// Scans generated materials and terrain layers on disk; one line per wrapper with an empty
		/// required slot ("path: slot, slot are empty"). Files that do not exist are skipped — a
		/// missing wrapper is <see cref="ProceduralArtPayload.State.MissingWrappers"/>'s business.
		/// </summary>
		public static List<string> Scan(IEnumerable<string> paths)
		{
			var lines = new List<string>();
			foreach (string path in paths)
			{
				string ext = Path.GetExtension(path).ToLowerInvariant();
				if ((ext != ".mat" && ext != ".terrainlayer") || !File.Exists(path))
				{
					continue;
				}
				string text;
				try
				{
					text = File.ReadAllText(path);
				}
				catch (IOException)
				{
					continue;
				}
				List<string> missing = MissingSlots(path, text);
				if (missing.Count > 0)
				{
					lines.Add($"{path}: {string.Join(", ", missing)} {(missing.Count == 1 ? "is" : "are")} empty");
				}
			}
			return lines;
		}
	}
}
#endif
