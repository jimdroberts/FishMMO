using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;

namespace FishMMO.Water
{
	/// <summary>
	/// The colour and clarity of each lake's and river's water by the biome of its shores (2026-10-16, phase I2e): the
	/// template's <see cref="BiomeTemplate.WaterLook"/>, read live, handed to each surface on its property block.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Which biome a body takes, and how a river's look runs into the water at its ends, is <see cref="BiomeWaterLooks"/>'s
	/// (the commonest land biome round its shores; a river carries the look of the water it leaves and joins at its ends, so
	/// where two meshes meet they are one colour). The shores are read from the scene's biome map when the surfaces are
	/// built; the biomes' templates are read when the looks are applied, and again whenever a template registers, so a look
	/// edited on a template shows on the next rebuild or registration, without a re-cut.
	/// </para>
	/// <para>
	/// A surface whose looks are all the material's own gets no look on its block (<c>_InlandLookOn</c> 0): drawn exactly as
	/// before. Every biome's look is authored as the material's (<see cref="BiomeWaterLooks.Today"/>), so nothing changes until
	/// a biome is given its own. A scene with no biome map (tools, probes) is drawn in the material's look.
	/// </para>
	/// </remarks>
	public sealed partial class InlandWaterRenderer
	{
		private static readonly int LooksId = Shader.PropertyToID("_InlandLooks");
		private static readonly int LookOnId = Shader.PropertyToID("_InlandLookOn");

		/// <summary>Vectors per look on the block (FishInlandWater.hlsl INLAND_LOOK_VECTORS).</summary>
		public const int LookVectors = 5;
		/// <summary>Looks per surface: its own, the water it leaves, the water it joins (FishInlandWater.hlsl _InlandLooks[15]).</summary>
		public const int LookSlots = 3;
		/// <summary>How near the material's own a look must be to be drawn as the material (its colours are stored to about 1e-7).</summary>
		private const float LookTolerance = 1e-3f;

		/// <summary>What stands round one body of water: the biome IDs on its shores and under its edge, weighted.</summary>
		private sealed class Shore
		{
			public readonly Dictionary<int, float> Along = new Dictionary<int, float>();
			public readonly Dictionary<int, float> Under = new Dictionary<int, float>();
		}

		private readonly List<(MeshRenderer Renderer, int Lake)> lakeRenderers = new List<(MeshRenderer, int)>();
		private readonly Dictionary<int, Shore> lakeShores = new Dictionary<int, Shore>();
		private readonly Dictionary<int, Shore> riverShores = new Dictionary<int, Shore>();
		private readonly Dictionary<int, SceneHydrology.River> riversById = new Dictionary<int, SceneHydrology.River>();
		private readonly Vector4[] lookVectors = new Vector4[LookVectors * LookSlots];
		private MaterialPropertyBlock lakeBlock;
		private bool biomesHooked;

		/// <summary>True when a river's first rows lie in the lake it leaves, whose water it then is (its look runs out of the lake's).</summary>
		public static bool LookFromStart(SceneHydrology.River river) => river != null && river.Start == SceneHydrology.End.Lake;

		/// <summary>True when a river's last rows lie in the lake or river it joins, whose water it then is.</summary>
		public static bool LookIntoEnd(SceneHydrology.River river)
			=> river != null && (river.Finish == SceneHydrology.End.Lake || river.Finish == SceneHydrology.End.Confluence);

		/// <summary>Reads what stands round every lake and along every river from the scene's biome map (none without one).</summary>
		private void PrepareLooks(SceneHydrology hydrology)
		{
			lakeShores.Clear();
			riverShores.Clear();
			riversById.Clear();
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				if (river != null && !riversById.ContainsKey(river.Id))
				{
					riversById[river.Id] = river;
				}
			}
			SceneBiomeMap map = SceneMap();
			if (map == null || map.Width <= 0 || map.Height <= 0)
			{
				return;
			}
			if (!biomesHooked)
			{
				BiomeRegistry.Changed += OnBiomesChanged;
				biomesHooked = true;
			}
			float mapCell = Mathf.Max(map.WorldSize.x / map.Width, map.WorldSize.y / map.Height);
			int IdAt(float x, float z) => map.IDAt(new Vector3(x, 0f, z));
			foreach (SceneHydrology.Lake lake in hydrology.Lakes)
			{
				if (lake == null || lakeShores.ContainsKey(lake.Id))
				{
					continue;
				}
				var shore = new Shore();
				BiomeWaterLooks.LakeShore(lake, IdAt, mapCell, shore.Along, shore.Under);
				lakeShores[lake.Id] = shore;
			}
			foreach (SceneHydrology.River river in riversById.Values)
			{
				var shore = new Shore();
				BiomeWaterLooks.RiverShore(river, IdAt, mapCell, shore.Along, shore.Under);
				riverShores[river.Id] = shore;
			}
		}

		/// <summary>The biome map of this renderer's scene (its WorldSceneSettings'), or null.</summary>
		private SceneBiomeMap SceneMap()
		{
			if (!gameObject.scene.IsValid())
			{
				return null;
			}
			foreach (GameObject root in gameObject.scene.GetRootGameObjects())
			{
				WorldSceneSettings settings = root.GetComponentInChildren<WorldSceneSettings>(true);
				if (settings != null)
				{
					return settings.BiomeMap;
				}
			}
			return null;
		}

		/// <summary>Templates register as they load, often after the water is built: the looks are applied again.</summary>
		private void OnBiomesChanged()
		{
			if (this != null && built.Count > 0)
			{
				ApplyFlow();
			}
		}

		private void UnhookBiomes()
		{
			if (biomesHooked)
			{
				BiomeRegistry.Changed -= OnBiomesChanged;
				biomesHooked = false;
			}
		}

		private static BiomeWaterRole? RoleOf(int id) => BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) && biome != null ? biome.WaterRole : (BiomeWaterRole?)null;

		/// <summary>The look of the water a body's shores give it: its biome's (<see cref="BiomeWaterLooks.Choose"/>), else <paramref name="fallback"/>.</summary>
		private static BiomeWaterLook LookOf(Shore shore, BiomeWaterLook fallback)
		{
			if (shore == null)
			{
				return fallback;
			}
			int id = BiomeWaterLooks.Choose(shore.Along, shore.Under, RoleOf);
			return id != 0 && BiomeRegistry.TryGetByID(id, out BiomeTemplate biome) ? BiomeWaterLooks.Of(biome) ?? fallback : fallback;
		}

		/// <summary>The material's own look, as its colours are stored (gamma), for comparing with the biomes'.</summary>
		private BiomeWaterLook MaterialLook()
		{
			if (Material == null)
			{
				return BiomeWaterLooks.Today;
			}
			Vector4 density = Material.GetVector("_WaterDensity");
			return new BiomeWaterLook(Material.GetColor("_ShallowColor"), Material.GetColor("_DeepColor"), new Vector3(density.x, density.y, density.z),
				Material.GetFloat("_Turbidity"), Material.GetColor("_SiltColor"), Material.GetColor("_FoamColor"));
		}

		private BiomeWaterLook LakeLook(int lakeId, BiomeWaterLook material)
			=> lakeShores.TryGetValue(lakeId, out Shore shore) ? LookOf(shore, material) : material;

		private BiomeWaterLook RiverOwnLook(int riverId, BiomeWaterLook material)
			=> riverShores.TryGetValue(riverId, out Shore shore) ? LookOf(shore, material) : material;

		/// <summary>
		/// A river's three looks: its own, the water it leaves (the lake it runs out of; its own where it rises) and the water it
		/// joins (the lake it enters, or the river it flows into as that river's water stands where they meet, its own blend of
		/// looks there; its own where it meets the sea or leaves the scene).
		/// </summary>
		/// <param name="deep">False for the river another joins, whose own end looks are taken as those waters' own (one step deep).</param>
		private void RiverLooks(SceneHydrology.River river, BiomeWaterLook material, bool deep, out BiomeWaterLook own, out BiomeWaterLook start, out BiomeWaterLook end)
		{
			own = RiverOwnLook(river.Id, material);
			start = LookFromStart(river) && river.StartLake >= 0 && lakeShores.ContainsKey(river.StartLake) ? LakeLook(river.StartLake, material) : own;
			end = own;
			if (river.Finish == SceneHydrology.End.Lake && river.EndLake >= 0 && lakeShores.ContainsKey(river.EndLake))
			{
				end = LakeLook(river.EndLake, material);
			}
			else if (river.Finish == SceneHydrology.End.Confluence && river.JoinsRiver >= 0 && river.JoinsRiver != river.Id
				&& riversById.TryGetValue(river.JoinsRiver, out SceneHydrology.River joined) && river.Points != null && river.Points.Length > 0)
			{
				end = deep ? LookWhere(joined, river.Points[river.Points.Length - 1], material) : RiverOwnLook(joined.Id, material);
			}
		}

		/// <summary>
		/// A river's look where its line passes nearest <paramref name="point"/>: its own blended with the water at its ends as
		/// its ribbon draws it there (the ends' own looks, one step deep).
		/// </summary>
		private BiomeWaterLook LookWhere(SceneHydrology.River river, Vector3 point, BiomeWaterLook material)
		{
			RiverLooks(river, material, false, out BiomeWaterLook own, out BiomeWaterLook start, out BiomeWaterLook end);
			if (river.Points == null || river.Points.Length < 2)
			{
				return own;
			}
			float length = 0f, along = 0f, nearest = float.PositiveInfinity;
			for (int i = 0; i < river.Points.Length; i++)
			{
				if (i > 0)
				{
					Vector3 step = river.Points[i] - river.Points[i - 1];
					step.y = 0f;
					length += step.magnitude;
				}
				float dx = river.Points[i].x - point.x, dz = river.Points[i].z - point.z;
				float distance = dx * dx + dz * dz;
				if (distance < nearest)
				{
					nearest = distance;
					along = length;
				}
			}
			float startWidth = river.Width != null && river.Width.Length > 0 ? river.Width[0] : 4f;
			float endWidth = river.Width != null && river.Width.Length > 0 ? river.Width[river.Width.Length - 1] : 4f;
			Vector2 share = BiomeWaterLooks.Blend(along, length, BiomeWaterLooks.BlendMetres(startWidth), BiomeWaterLooks.BlendMetres(endWidth),
				LookFromStart(river), LookIntoEnd(river));
			BiomeWaterLook look = own;
			float ownShare = 1f - share.x - share.y;
			if (share.x > 0f)
			{
				look = BiomeWaterLook.Lerp(look, start, share.x / Mathf.Max(1e-6f, ownShare + share.x));
			}
			if (share.y > 0f)
			{
				look = BiomeWaterLook.Lerp(look, end, share.y);
			}
			return look;
		}

		/// <summary>Sets a river's looks on its property block; false (nothing set) where all three are the material's own.</summary>
		private bool ApplyRiverLook(int riverId, MaterialPropertyBlock block)
		{
			if (!riversById.TryGetValue(riverId, out SceneHydrology.River river))
			{
				return false;
			}
			BiomeWaterLook material = MaterialLook();
			RiverLooks(river, material, true, out BiomeWaterLook own, out BiomeWaterLook start, out BiomeWaterLook end);
			if (own.Approximately(material, LookTolerance) && start.Approximately(material, LookTolerance) && end.Approximately(material, LookTolerance))
			{
				return false;
			}
			block.SetVectorArray(LooksId, Pack(own, start, end));
			block.SetFloat(LookOnId, 1f);
			return true;
		}

		/// <summary>Gives each lake its look on its own block, or none where its look is the material's.</summary>
		private void ApplyLakeLooks()
		{
			BiomeWaterLook material = MaterialLook();
			lakeBlock ??= new MaterialPropertyBlock();
			foreach ((MeshRenderer renderer, int lake) in lakeRenderers)
			{
				if (renderer == null)
				{
					continue;
				}
				BiomeWaterLook look = LakeLook(lake, material);
				if (look.Approximately(material, LookTolerance))
				{
					renderer.SetPropertyBlock(null);
					continue;
				}
				lakeBlock.Clear();
				lakeBlock.SetVectorArray(LooksId, Pack(look, look, look));
				lakeBlock.SetFloat(LookOnId, 1f);
				renderer.SetPropertyBlock(lakeBlock);
			}
		}

		/// <summary>
		/// The three looks as the shader reads them (FishInlandWater.hlsl InlandLookAt): per look, shallow rgb + silt carried,
		/// deep, absorption per metre, silt colour, foam; colours made linear where the project renders in linear space, as
		/// Unity does to a material's colours.
		/// </summary>
		private Vector4[] Pack(BiomeWaterLook own, BiomeWaterLook start, BiomeWaterLook end)
		{
			bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
			Vector4 C(Color c, float w)
			{
				Color shown = linear ? c.linear : c;
				return new Vector4(shown.r, shown.g, shown.b, w);
			}
			void Put(int slot, BiomeWaterLook look)
			{
				int at = slot * LookVectors;
				lookVectors[at] = C(look.Shallow, look.Turbidity);
				lookVectors[at + 1] = C(look.Deep, 0f);
				lookVectors[at + 2] = new Vector4(look.Absorption.x, look.Absorption.y, look.Absorption.z, 0f);
				lookVectors[at + 3] = C(look.Silt, 0f);
				lookVectors[at + 4] = C(look.Foam, 0f);
			}
			Put(0, own);
			Put(1, start);
			Put(2, end);
			return lookVectors;
		}
	}
}
