#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>One ground a cave's floor wears: the biome's own layer in that role, else a generated ground family.</summary>
	public struct CaveRockLayer
	{
		/// <summary>The biome's own ground layer (its art: a TerrainLayer or its textures), or null.</summary>
		public TerrainTextureLayer Biome;
		/// <summary>The generated ground family (<see cref="Ground"/>) worn when the biome has no layer in the role.</summary>
		public string Family;

		/// <summary>What the layer is, for a material's name: the biome layer's family or the generated one's.</summary>
		public string Name => Biome != null ? CaveRockLayers.FamilyOf(Biome) ?? "Biome" : Family;
	}

	/// <summary>
	/// How a cave's rock is dressed by its habitat (FishCaveRock.hlsl): the grounds its floors take, the stains its walls take
	/// and the colour its glow crust gives off.
	/// </summary>
	public struct CaveRockLook
	{
		/// <summary>Fine sediment in the floor's hollows: silt, mud (darkened and glossed by the wet), a sea cave's sand.</summary>
		public CaveRockLayer Silt;
		/// <summary>Coarse sediment: grit and gravel where water ran and rock fell, a mine's spoil, a lava tube's cinder.</summary>
		public CaveRockLayer Gravel;
		/// <summary>Humus and leaf litter near a mouth (in the dark the shader tints it to <see cref="Guano"/>).</summary>
		public CaveRockLayer Organic;
		/// <summary>The packed ground of a way worn by feet.</summary>
		public CaveRockLayer Trodden;
		/// <summary>The mineral streaks the seepage leaves down the walls (iron ochre; a mine's ore rust).</summary>
		public Color Stain;
		/// <summary>The second streak: calcite flowstone crust (a mine's verdigris, a sea cave's salt).</summary>
		public Color Crust;
		/// <summary>How strongly the walls are streaked, 0..1.</summary>
		public float StainStrength;
		/// <summary>The glow crust's colour (linear), and its brightness (HDR scale).</summary>
		public Color Glow;
		public float GlowStrength;
		/// <summary>Guano: the organic layer's colour deep in the dark, under the roosts.</summary>
		public Color Guano;
		/// <summary>How much moss and lichen grow in the twilight zone, 0..1+.</summary>
		public float Moss;
		/// <summary>How glossy the wet makes the rock, 0..1.</summary>
		public float WetGloss;

		/// <summary>A key of the whole look, for the material's name: equal looks share one material.</summary>
		public string Key
		{
			get
			{
				unchecked
				{
					uint h = 2166136261u;
					void Mix(string s)
					{
						foreach (char c in s ?? string.Empty)
						{
							h = (h ^ c) * 16777619u;
						}
						h = (h ^ 0x7Cu) * 16777619u;
					}
					void MixF(float f) => Mix(Mathf.RoundToInt(f * 1000f).ToString(System.Globalization.CultureInfo.InvariantCulture));
					Mix(Silt.Name);
					Mix(Gravel.Name);
					Mix(Organic.Name);
					Mix(Trodden.Name);
					foreach (Color c in new[] { Stain, Crust, Glow, Guano })
					{
						MixF(c.r);
						MixF(c.g);
						MixF(c.b);
					}
					MixF(StainStrength);
					MixF(GlowStrength);
					MixF(Moss);
					MixF(WetGloss);
					return h.ToString("x8");
				}
			}
		}
	}

	/// <summary>
	/// The one lookup of how a cave's rock is dressed (2026-10-20): by the biome its host site stands in and the cave's form.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The floors take the biome's own grounds where it has one in the role, matched by what the ground IS (its generated family:
	/// a layer whose art is <c>Ground_Mud</c> is mud whatever slot it is in): silt, mud or clay for the fine sediment; gravel or
	/// pebbles (or the biome's gravel bar) for the coarse; forest floor, needle litter, jungle floor, peat, sphagnum or heath for
	/// the litter. Where the biome has none, the generated ground of the form's own choosing. The trodden ground, the stains, the
	/// glow's colour, the guano and the moss are the form's alone for now.
	/// </para>
	/// <para>
	/// Kept to this one function so the biome suite's cave interiors (I2n, decisions 37(b)/51/57: cave floor layers by surface
	/// angle, walls and roofs the shell's rock) can move each choice into template fields without touching the shaper or the
	/// shader: an interior biome's own floor grounds, its glow colour (decision 37(c) caveGlow), its stain.
	/// </para>
	/// </remarks>
	public static class CaveRockLayers
	{
		private static readonly string[] SiltFamilies = { Ground.Silt, Ground.Mud, Ground.Clay };
		private static readonly string[] GravelFamilies = { Ground.Gravel, Ground.Pebbles };
		private static readonly string[] OrganicFamilies = { Ground.ForestFloor, Ground.NeedleLitter, Ground.JungleFloor, Ground.Peat, Ground.Sphagnum, Ground.Heath };

		/// <summary>The look of a cave of <paramref name="form"/> in <paramref name="biome"/> (null: the form's defaults).</summary>
		public static CaveRockLook For(BiomeTemplate biome, CaveForm form)
		{
			var look = new CaveRockLook
			{
				Stain = new Color(0.62f, 0.42f, 0.24f),
				Crust = new Color(0.9f, 0.86f, 0.77f),
				StainStrength = 0.7f,
				Glow = new Color(0.32f, 1f, 0.72f),
				GlowStrength = 2.2f,
				Guano = new Color(0.52f, 0.46f, 0.36f),
				Moss = 1f,
				WetGloss = 1f,
			};
			string silt = Ground.Silt, gravel = Ground.Gravel, organic = Ground.ForestFloor, trodden = Ground.PathGrit;
			bool biomeSilt = true, biomeGravel = true;
			switch (form)
			{
				case CaveForm.Grotto:
					look.Moss = 1.2f;
					break;
				case CaveForm.SeaCave:
					// Sand and shingle the waves bring in, salt and weed on the walls, blue-green glow in the spray.
					silt = Ground.SandBeach;
					gravel = Ground.Pebbles;
					trodden = Ground.PathSand;
					biomeSilt = false;
					look.Stain = new Color(0.22f, 0.27f, 0.2f);
					look.Crust = new Color(0.86f, 0.86f, 0.82f);
					look.StainStrength = 0.6f;
					look.Glow = new Color(0.3f, 0.85f, 1f);
					look.GlowStrength = 1.8f;
					look.Moss = 0.6f;
					break;
				case CaveForm.IceCave:
					// Frozen silt and grit; a faint blue glow-ice deep in; little stain on ice.
					look.Stain = new Color(0.55f, 0.62f, 0.7f);
					look.Crust = new Color(0.92f, 0.96f, 1f);
					look.StainStrength = 0.2f;
					look.Glow = new Color(0.45f, 0.78f, 1f);
					look.GlowStrength = 1.4f;
					look.Moss = 0.15f;
					look.WetGloss = 0.6f;
					break;
				case CaveForm.LavaTube:
					// Ash and cinder; gypsum and sulphur crusts, rust where water ever got in; no glow.
					silt = Ground.Ash;
					gravel = Ground.Cinder;
					trodden = Ground.PathAsh;
					biomeSilt = false;
					biomeGravel = false;
					look.Stain = new Color(0.5f, 0.27f, 0.15f);
					look.Crust = new Color(0.86f, 0.82f, 0.62f);
					look.StainStrength = 0.4f;
					look.GlowStrength = 0f;
					look.Moss = 0.7f;
					break;
				case CaveForm.Mine:
					// Trodden dirt and spoil gravel; ore-stained walls (rust and verdigris); little glow.
					silt = Ground.Clay;
					trodden = Ground.PathDryEarth;
					look.Stain = new Color(0.7f, 0.38f, 0.16f);
					look.Crust = new Color(0.34f, 0.6f, 0.5f);
					look.StainStrength = 0.85f;
					look.Glow = new Color(0.62f, 1f, 0.4f);
					look.GlowStrength = 1.2f;
					look.Moss = 0.5f;
					break;
			}
			look.Silt = new CaveRockLayer { Biome = biomeSilt ? Find(biome, SiltFamilies) : null, Family = silt };
			TerrainTextureLayer bar = biome != null && biome.BarGravelTextureLayer != null && biome.BarGravelTextureLayer.HasAlbedo ? biome.BarGravelTextureLayer : null;
			look.Gravel = new CaveRockLayer { Biome = biomeGravel ? Find(biome, GravelFamilies) ?? bar : null, Family = gravel };
			look.Organic = new CaveRockLayer { Biome = Find(biome, OrganicFamilies), Family = organic };
			look.Trodden = new CaveRockLayer { Family = trodden };
			return look;
		}

		/// <summary>
		/// What a biome layer is made of: its art's generated family (<c>Ground_&lt;Family&gt;</c>, the TerrainLayer's name or
		/// its albedo's), or null for art that is not a generated ground.
		/// </summary>
		public static string FamilyOf(TerrainTextureLayer layer)
		{
			if (layer == null)
			{
				return null;
			}
			string name = layer.terrainLayer != null ? layer.terrainLayer.name : layer.albedoTexture != null ? layer.albedoTexture.name : null;
			const string prefix = "Ground_";
			if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, System.StringComparison.Ordinal))
			{
				return null;
			}
			string rest = name.Substring(prefix.Length);
			int cut = rest.IndexOf('_');
			return cut > 0 ? rest.Substring(0, cut) : rest;
		}

		/// <summary>The biome's first ground layer (main, details, beds, bars) made of one of <paramref name="families"/>, in their order.</summary>
		private static TerrainTextureLayer Find(BiomeTemplate biome, string[] families)
		{
			if (biome == null)
			{
				return null;
			}
			List<TerrainTextureLayer> layers = biome.GetAllTextureLayersInOrder();
			foreach (string family in families)
			{
				foreach (TerrainTextureLayer layer in layers)
				{
					if (layer != null && layer.HasAlbedo && FamilyOf(layer) == family)
					{
						return layer;
					}
				}
			}
			return null;
		}
	}

	/// <summary>
	/// The cave rock's material: the shell rock's own material with the habitat's layers on (keyword <c>_FISH_CAVE_ROCK</c>,
	/// FishCaveRock.hlsl), one per shell and look, kept as a sub-asset of the scene's caves asset beside the meshes.
	/// </summary>
	public static class CaveRockMaterials
	{
		public const string Keyword = "_FISH_CAVE_ROCK";
		private const string Prefix = "Cave Rock ";

		/// <summary>
		/// The cave rock material for a shell's material and a look: rewritten in place in <paramref name="assetPath"/> when it
		/// has changed (references survive), added when it is new; in memory only for a null path. Null without a shell material.
		/// </summary>
		/// <param name="written">True when the asset was changed (it then needs saving).</param>
		public static Material For(Material shell, in CaveRockLook look, string assetPath, out bool written)
		{
			written = false;
			if (shell == null)
			{
				return null;
			}
			var material = new Material(shell) { name = $"{Prefix}{shell.name} {look.Key}" };
			Apply(material, in look);
			if (string.IsNullOrEmpty(assetPath) || AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
			{
				return material;
			}
			foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
			{
				if (asset is Material existing && existing.name == material.name)
				{
					if (existing.shader != material.shader || existing.ComputeCRC() != material.ComputeCRC()
						|| !existing.IsKeywordEnabled(Keyword))
					{
						EditorUtility.CopySerialized(material, existing);
						EditorUtility.SetDirty(existing);
						written = true;
					}
					Object.DestroyImmediate(material);
					return existing;
				}
			}
			AssetDatabase.AddObjectToAsset(material, assetPath);
			written = true;
			return material;
		}

		/// <summary>The look's textures and figures onto a material (the shell's copy), and its keyword on.</summary>
		public static void Apply(Material material, in CaveRockLook look)
		{
			Resolve(look.Silt, out Texture2D siltAlbedo, out Texture2D siltNormal, out float siltTile);
			Resolve(look.Gravel, out Texture2D gravelAlbedo, out Texture2D gravelNormal, out float gravelTile);
			Resolve(look.Organic, out Texture2D organicAlbedo, out _, out float organicTile);
			Resolve(look.Trodden, out Texture2D troddenAlbedo, out _, out float troddenTile);
			Set(material, "_FishCaveSiltMap", siltAlbedo);
			Set(material, "_FishCaveSiltNormal", siltNormal);
			Set(material, "_FishCaveGravelMap", gravelAlbedo);
			Set(material, "_FishCaveGravelNormal", gravelNormal);
			Set(material, "_FishCaveOrganicMap", organicAlbedo);
			Set(material, "_FishCaveTroddenMap", troddenAlbedo);
			// Sediment's grounds are laid finer than the open ground's (a cave floor is seen from a stride away): 0.6 of their tile.
			material.SetVector("_FishCaveTiles", new Vector4(1f / (0.6f * siltTile), 1f / (0.6f * gravelTile), 1f / (0.6f * organicTile), 1f / (0.6f * troddenTile)));
			material.SetColor("_FishCaveStain", new Color(look.Stain.r, look.Stain.g, look.Stain.b, look.StainStrength));
			material.SetColor("_FishCaveCrust", look.Crust);
			material.SetColor("_FishCaveGlow", new Color(look.Glow.r * look.GlowStrength, look.Glow.g * look.GlowStrength, look.Glow.b * look.GlowStrength, 1f));
			material.SetColor("_FishCaveGuano", look.Guano);
			// x moss in the twilight, y wet gloss, z 1 when the ground textures are bound (else flat colours), w unused.
			bool bound = siltAlbedo != null && gravelAlbedo != null && organicAlbedo != null && troddenAlbedo != null;
			material.SetVector("_FishCaveParams", new Vector4(look.Moss, look.WetGloss, bound ? 1f : 0f, 0f));
			material.EnableKeyword(Keyword);
			// Cave rock is never drawn by the GPU-instanced twin: the keyword's code is the plain shader's only.
		}

		private static void Set(Material material, string property, Texture texture)
		{
			if (material.HasProperty(property))
			{
				material.SetTexture(property, texture);
			}
		}

		/// <summary>A layer's albedo, normal and tile size (metres): the biome layer's art, else the generated family's layer.</summary>
		private static void Resolve(in CaveRockLayer layer, out Texture2D albedo, out Texture2D normal, out float tile)
		{
			albedo = null;
			normal = null;
			tile = 4f;
			TerrainTextureLayer biome = layer.Biome;
			if (biome != null)
			{
				if (biome.terrainLayer != null && biome.terrainLayer.diffuseTexture != null)
				{
					albedo = biome.terrainLayer.diffuseTexture;
					normal = biome.terrainLayer.normalMapTexture;
					tile = biome.terrainLayer.tileSize.x;
				}
				else
				{
					albedo = biome.albedoTexture;
					normal = biome.normalTexture;
					tile = biome.tileSize.x;
				}
			}
			if (albedo == null && !string.IsNullOrEmpty(layer.Family))
			{
				var generated = AssetDatabase.LoadAssetAtPath<TerrainLayer>(ProceduralArtCatalogue.GroundLayerPath(layer.Family));
				if (generated != null)
				{
					albedo = generated.diffuseTexture;
					normal = generated.normalMapTexture;
					tile = generated.tileSize.x;
				}
			}
			tile = Mathf.Max(0.5f, tile);
		}
	}
}
#endif
