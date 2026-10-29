using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.Client
{
	/// <summary>
	/// Look families: the materials of a renderer's prototypes that differ only in their look-array slices and a few scalars (the
	/// props' Structure_*, Rock_*, Ice_* and cave glow sets and tints) drawn through ONE family material per family, so the
	/// material set-up Unity does for every draw outside the SRP Batcher (StdRender.ApplyShader) happens once a family, not once a
	/// texture set.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A family</b> is every clone (<see cref="IndirectOf"/>) whose material is array-backed (<see cref="TerrainLookMath.Keyword"/>:
	/// the art generator wrote its maps as slices of a surface family's Texture2DArray assets), whose shader has the table variant
	/// (<see cref="TerrainLookMath.TableKeyword"/>), whose keywords allow it (opaque, not alpha-tested:
	/// <see cref="TerrainLookMath.KeywordsAllowLooks"/>), and whose every property but the look's own
	/// (<see cref="TerrainLookMath.LookProperties"/>) is equal — the arrays too, by identity. Its material is a copy of its first
	/// member's clone with the table keyword on and the table bound; the members' clones stay, and are drawn again when looks are
	/// switched off (<see cref="LookArrays"/>) or a pool is still merging.
	/// </para>
	/// <para>
	/// <b>Nothing is copied.</b> The members' textures are the arrays every one of them already references; the family material
	/// keeps them. What a look adds is one row of the family's table (<see cref="TerrainLookMath.WriteRow"/>, RGBAHalf, 56 bytes).
	/// </para>
	/// <para>
	/// <b>Which look a vertex wears</b> rides in the merged meshes (<see cref="MeshPool"/>): a part is pooled once per look it is
	/// drawn with, its uv3.x the look and its uv0 already tiled by the look's <c>_BaseMap</c> scale and offset
	/// (<see cref="TerrainLookMath.BakeLook"/>). So a look command draws from its pool on every platform, batched or not. (A
	/// DOTS-instanced draw may instead name the row per instance, <c>_FishLook</c>; FishWeatherLitInput.hlsl.)
	/// </para>
	/// </remarks>
	public sealed partial class TerrainGpuRenderer
	{
		/// <summary>
		/// Whether families draw through their family materials (probe variant `nolooks` switches it off: every member's own clone
		/// again, the draws as before; the textures are the same arrays either way).
		/// </summary>
		public static bool LookArrays = true;

		private static readonly int LookTableId = Shader.PropertyToID(TerrainLookMath.TableName);
		private static readonly int LookSlicesId = Shader.PropertyToID(TerrainLookMath.SlicesName);
		private static readonly int LookEmissionSliceId = Shader.PropertyToID(TerrainLookMath.EmissionSliceName);

		private sealed class LookFamily
		{
			public string Key;
			public Material Material;
			public readonly List<TerrainLookMath.LookValues> Looks = new List<TerrainLookMath.LookValues>();
			public Texture2D Table;
			public Color[] TablePixels;
			public bool Dirty = true;
		}

		/// <summary>A clone's look: its family and its row there, or no family (the clone draws as itself).</summary>
		private readonly struct LookRef
		{
			public readonly LookFamily Family;
			public readonly int Look;

			public LookRef(LookFamily family, int look)
			{
				Family = family;
				Look = look;
			}
		}

		private readonly Dictionary<string, LookFamily> lookFamilies = new Dictionary<string, LookFamily>(StringComparer.Ordinal);
		private readonly List<LookFamily> lookFamilyList = new List<LookFamily>();
		private readonly Dictionary<Material, LookRef> looksOf = new Dictionary<Material, LookRef>();
		private readonly StringBuilder lookKey = new StringBuilder(1024);

		/// <summary>The layout's: look commands are drawn through their family materials (<see cref="LooksReady"/>).</summary>
		private bool looksState;
		/// <summary><see cref="LookArrays"/> as the last layout read it, and whether a look pool was still merging then (the next camera lays out again).</summary>
		private bool looksWanted, looksPending;

		/// <summary>Whether the next camera must lay out again for the looks: switched, or pools still merging.</summary>
		private bool LooksChanged => looksPending || looksWanted != LookArrays;

		/// <summary>Families drawn through one material, their looks, and the GPU bytes of their tables (diagnostics, ScenePerfProbe).</summary>
		public int LookFamilyCount => lookFamilyList.Count;
		public int LookCount
		{
			get
			{
				int n = 0;
				foreach (LookFamily f in lookFamilyList)
				{
					n += f.Looks.Count;
				}
				return n;
			}
		}
		public long LookTableBytes { get; private set; }

		/// <summary>
		/// The family material and row a clone draws with, or false when it draws as itself (not array-backed, a blocking keyword,
		/// no table variant). Decided once per clone.
		/// </summary>
		private bool LookFor(Material clone, out Material family, out int look)
		{
			family = null;
			look = -1;
			if (clone == null)
			{
				return false;
			}
			if (!looksOf.TryGetValue(clone, out LookRef found))
			{
				found = MakeLook(clone);
				looksOf.Add(clone, found);
			}
			if (found.Family == null || found.Family.Material == null)
			{
				return false;
			}
			family = found.Family.Material;
			look = found.Look;
			return true;
		}

		private LookRef MakeLook(Material clone)
		{
			if (clone.shader == null || !clone.IsKeywordEnabled(TerrainLookMath.Keyword)
				|| !clone.shader.keywordSpace.FindKeyword(TerrainLookMath.TableKeyword).isValid
				|| !TerrainLookMath.KeywordsAllowLooks(clone.shaderKeywords) || clone.renderQueue >= (int)RenderQueue.AlphaTest
				|| (clone.HasProperty("_Surface") && clone.GetFloat("_Surface") != 0f) || !clone.HasProperty(LookSlicesId))
			{
				return default;
			}
			string key = FamilyKey(clone);
			if (key == null)
			{
				return default;
			}
			LookFamily family = FamilyFor(key, clone);

			TerrainLookMath.LookValues values = TerrainLookMath.LookValues.Untextured();
			Vector2 scale = clone.HasProperty("_BaseMap") ? clone.GetTextureScale("_BaseMap") : Vector2.one;
			Vector2 offset = clone.HasProperty("_BaseMap") ? clone.GetTextureOffset("_BaseMap") : Vector2.zero;
			values.BaseMapST = new Vector4(scale.x, scale.y, offset.x, offset.y);
			values.BaseColor = ColorOf(clone, "_BaseColor", Color.white);
			values.SpecColor = ColorOf(clone, "_SpecColor", new Color(0.2f, 0.2f, 0.2f, 1f));
			values.EmissionColor = ColorOf(clone, "_EmissionColor", Color.black);
			values.Cutoff = FloatOf(clone, "_Cutoff", 0.5f);
			values.Smoothness = FloatOf(clone, "_Smoothness", 0.5f);
			values.Metallic = FloatOf(clone, "_Metallic", 0f);
			values.BumpScale = FloatOf(clone, "_BumpScale", 1f);
			values.OcclusionStrength = FloatOf(clone, "_OcclusionStrength", 1f);
			values.Moss = FloatOf(clone, "_FishMoss", 0f);
			values.Structure = FloatOf(clone, "_FishStructure", 0f);
			values.WeatherAmount = FloatOf(clone, "_FishWeatherAmount", 1f);
			values.SetSlices(clone.GetVector(LookSlicesId), clone.HasProperty(LookEmissionSliceId) ? clone.GetFloat(LookEmissionSliceId) : -1f);
			family.Looks.Add(values);
			family.Dirty = true;
			return new LookRef(family, family.Looks.Count - 1);
		}

		private LookFamily FamilyFor(string key, Material clone)
		{
			if (lookFamilies.TryGetValue(key, out LookFamily family))
			{
				return family;
			}
			// The first member's clone, arrays and all (the same assets every member references), drawn through the table.
			var material = new Material(clone) { name = clone.name + " (looks)", hideFlags = HideFlags.DontSave };
			material.EnableKeyword(TerrainLookMath.TableKeyword);
			family = new LookFamily { Key = key, Material = material };
			lookFamilies.Add(key, family);
			lookFamilyList.Add(family);
			BindLookMaterial(family.Material);
			return family;
		}

		private static Color ColorOf(Material m, string property, Color fallback) => m.HasProperty(property) ? m.GetColor(property) : fallback;
		private static float FloatOf(Material m, string property, float fallback) => m.HasProperty(property) ? m.GetFloat(property) : fallback;

		/// <summary>
		/// Everything a family's members must share, as one string: the shader, keywords, queue, passes, and every property not the
		/// look's (values exactly, textures — the look arrays among them — by identity and tiling). Null when a property cannot be read.
		/// </summary>
		private string FamilyKey(Material clone)
		{
			StringBuilder sb = lookKey;
			sb.Clear();
			Shader shader = clone.shader;
			sb.Append(shader.GetInstanceID()).Append('|').Append(clone.renderQueue).Append('|');
			// The shader's own keywords only: a stale one it no longer declares (the structure kit's _FISHSTRUCTURE_ON, 154 of the
			// Structure_* materials) changes nothing it draws, so it must not split a family.
			LocalKeyword[] enabled = clone.enabledKeywords;
			var keywords = new string[enabled.Length];
			for (int i = 0; i < enabled.Length; i++)
			{
				keywords[i] = enabled[i].name;
			}
			Array.Sort(keywords, StringComparer.Ordinal);
			foreach (string k in keywords)
			{
				sb.Append(k).Append(',');
			}
			sb.Append('|');
			for (int p = 0; p < shader.passCount; p++)
			{
				string mode = shader.FindPassTagValue(p, LightModeTag).name;
				sb.Append(clone.GetShaderPassEnabled(mode) ? '1' : '0');
			}
			int count = shader.GetPropertyCount();
			for (int i = 0; i < count; i++)
			{
				string name = shader.GetPropertyName(i);
				if (TerrainLookMath.IsLookOrIgnored(name))
				{
					continue;
				}
				sb.Append('|').Append(name).Append('=');
				switch (shader.GetPropertyType(i))
				{
					case ShaderPropertyType.Color:
						sb.Append(clone.GetColor(name).ToString("R"));
						break;
					case ShaderPropertyType.Vector:
						sb.Append(clone.GetVector(name).ToString("R"));
						break;
					case ShaderPropertyType.Float:
					case ShaderPropertyType.Range:
						sb.Append(clone.GetFloat(name).ToString("R"));
						break;
					case ShaderPropertyType.Int:
						sb.Append(clone.GetInteger(name));
						break;
					case ShaderPropertyType.Texture:
						Texture t = clone.GetTexture(name);
						sb.Append(t != null ? t.GetInstanceID() : 0);
						if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.NoScaleOffset) == 0)
						{
							sb.Append('@').Append(clone.GetTextureScale(name).ToString("R")).Append(clone.GetTextureOffset(name).ToString("R"));
						}
						break;
					default:
						return null;
				}
			}
			return sb.ToString();
		}

		private static readonly ShaderTagId LightModeTag = new ShaderTagId("LightMode");

		/// <summary>
		/// Called at the head of a layout: whether this layout draws its look parts through their families. Registers every look
		/// part's pool and merges what the budget allows, writes the tables of families that took new looks, and remembers whether
		/// pools were left (the next camera lays out again).
		/// </summary>
		private bool LooksReady()
		{
			looksWanted = LookArrays;
			looksPending = false;
			if (!LookArrays)
			{
				return false;
			}
			bool any = false;
			foreach (Model m in models)
			{
				foreach (PartSource[] level in m.Levels)
				{
					foreach (PartSource part in level)
					{
						if (LookFor(part.Material, out Material family, out int look) && PoolOf(family, part.Mesh, part.Submesh, look) != null)
						{
							any = true;
						}
					}
				}
			}
			if (!any)
			{
				return false;
			}
			BuildLookTables();
			if (!MergePools())
			{
				looksPending = true;
				return false;
			}
			return true;
		}

		/// <summary>The draw a look part takes: its family's material and look when this layout draws looks and its mesh can be pooled.</summary>
		private bool LookCommand(in PartSource part, out Material family, out int look)
		{
			family = null;
			look = -1;
			return looksState && LookFor(part.Material, out family, out look) && PoolOf(family, part.Mesh, part.Submesh, look) != null;
		}

		/// <summary>Unbatched layouts: each look command draws its part's submesh of its (merged) pool, the only mesh that carries its look.</summary>
		private void PoolLookCommands()
		{
			for (int c = 0; c < commands.Count; c++)
			{
				Command command = commands[c];
				if (command.Look < 0)
				{
					continue;
				}
				MeshPool pool = PoolOf(command.Params.material, command.SourceMesh, command.SourceSubmesh, command.Look);
				command.Mesh = pool.Mesh;
				command.Submesh = pool.Index[(command.SourceMesh, command.SourceSubmesh, command.Look)];
				commands[c] = command;
			}
		}

		/// <summary>Bakes each look part's look into a freshly merged pool mesh (uv3.x, and uv0 tiled), when the pool is a family's.</summary>
		private static void BakePoolLooks(MeshPool pool, Mesh mesh)
		{
			if (!pool.HasLooks)
			{
				return;
			}
			var uv0List = new List<Vector2>();
			mesh.GetUVs(0, uv0List);
			Vector2[] uv0 = uv0List.Count == mesh.vertexCount ? uv0List.ToArray() : null;
			var uv3 = new Vector2[mesh.vertexCount];
			var baked = new bool[mesh.vertexCount];
			for (int i = 0; i < pool.Parts.Count; i++)
			{
				int look = pool.Parts[i].Look;
				Vector4 st = pool.Family != null && look >= 0 && look < pool.Family.Looks.Count ? pool.Family.Looks[look].BaseMapST : new Vector4(1f, 1f, 0f, 0f);
				TerrainLookMath.BakeLook(uv0, uv3, baked, mesh.GetIndices(i, true), st, look);
			}
			if (uv0 != null)
			{
				mesh.SetUVs(0, uv0);
			}
			mesh.SetUVs(3, uv3);
		}

		/// <summary>Creates or grows the tables of the families that took new looks and writes their rows: the only GPU data a family adds.</summary>
		private void BuildLookTables()
		{
			bool changed = false;
			foreach (LookFamily family in lookFamilyList)
			{
				if (!family.Dirty)
				{
					continue;
				}
				family.Dirty = false;
				changed = true;
				int rows = TerrainLookMath.TableRows(family.Looks.Count);
				if (family.Table == null || family.Table.height < rows)
				{
					if (family.Table != null)
					{
						Object.Destroy(family.Table);
					}
					family.Table = new Texture2D(TerrainLookMath.TableColumns, rows, TextureFormat.RGBAHalf, false, true)
					{
						name = family.Material.name + " table",
						hideFlags = HideFlags.DontSave,
						filterMode = FilterMode.Point,
						wrapMode = TextureWrapMode.Clamp,
					};
					family.TablePixels = new Color[TerrainLookMath.TableColumns * rows];
				}
				for (int l = 0; l < family.Looks.Count; l++)
				{
					TerrainLookMath.WriteRow(family.Looks[l], family.TablePixels, TerrainLookMath.TableColumns, l);
				}
				family.Table.SetPixels(family.TablePixels);
				family.Table.Apply(false, false);
				family.Material.SetTexture(LookTableId, family.Table);
			}
			if (changed)
			{
				long bytes = 0;
				foreach (LookFamily family in lookFamilyList)
				{
					bytes += family.Table != null ? TerrainLookMath.TableBytes(family.Table.height) : 0;
				}
				LookTableBytes = bytes;
				Debug.Log($"[Terrain GPU] look families: {LookCount} looks in {lookFamilyList.Count} famil{(lookFamilyList.Count == 1 ? "y" : "ies")}, {bytes / 1024.0:F1} KB of tables (the textures are the generated look arrays: nothing copied).");
			}
		}

		/// <summary>The family materials take the same buffers and per-renderer switches as the clones (<see cref="BindBlocks"/>).</summary>
		private void BindLooks()
		{
			foreach (LookFamily family in lookFamilyList)
			{
				BindLookMaterial(family.Material);
			}
		}

		/// <summary>The family whose material this is, or null (a pool is a family's when its material is).</summary>
		private LookFamily LookFamilyOf(Material material)
		{
			foreach (LookFamily family in lookFamilyList)
			{
				if (family.Material == material)
				{
					return family;
				}
			}
			return null;
		}

		private void BindLookMaterial(Material material)
		{
			if (material == null)
			{
				return;
			}
			material.SetFloat(ContactOnId, ContactMode);
			material.SetFloat(FloraOnId, FloraMode);
			if (instances != null)
			{
				material.SetBuffer(InstancesId, instances);
			}
			if (visibleBuffer != null)
			{
				material.SetBuffer(VisibleId, visibleBuffer);
			}
			if (commandBaseBuffer != null)
			{
				material.SetBuffer(CommandBasesId, commandBaseBuffer);
			}
		}

		private void DisposeLooks()
		{
			foreach (LookFamily family in lookFamilyList)
			{
				if (family.Material != null)
				{
					Object.Destroy(family.Material);
				}
				if (family.Table != null)
				{
					Object.Destroy(family.Table);
				}
			}
			lookFamilyList.Clear();
			lookFamilies.Clear();
			looksOf.Clear();
			LookTableBytes = 0;
		}
	}
}
