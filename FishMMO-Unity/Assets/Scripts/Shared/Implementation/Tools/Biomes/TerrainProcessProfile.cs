using System;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// How a kind of ground wears: what the scene generator's erosion, drainage and plateau passes
	/// read to shape it. Every value is relative to temperate soil on moderate rock, which is 1 (or
	/// the default) throughout.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The ground's part, not the weather's or the rock's.</b> How much rain falls comes from the
	/// climate (<see cref="MoistureModel.Precipitation"/>), and how hard the rock is from the planet's
	/// geology; what a biome adds is how its surface answers both — roots that hold soil, a crust
	/// that sheds water, scree that will not stand steeper than its angle. So a forest and a
	/// grassland under the same rain on the same rock still wear differently.
	/// </para>
	/// <para>
	/// <b>Every field blends.</b> A scene mixes biomes across soft boundaries, and the generator mixes
	/// these numbers the same way, so erosion changes character gradually rather than stamping biome
	/// edges into the ground. That is why choices that read as yes or no — does the ground keep its
	/// hollows, does it break into benches — are shares from 0 to 1.
	/// </para>
	/// </remarks>
	[Serializable]
	public struct TerrainProcess
	{
		[Tooltip("How readily running water carries the surface away, against temperate soil (1). The rock's own hardness multiplies in separately.")]
		[Range(0f, 3f)] public float Erodibility;

		[Tooltip("Share of that wash the vegetation's roots and mats hold back: 0 bare ground … 1 fully bound.")]
		[Range(0f, 1f)] public float VegetationCohesion;

		[Tooltip("How much of the rain arrives as downpours, against temperate rain (1). Flash floods cut far more than the same water falling gently.")]
		[Range(0f, 3f)] public float Storminess;

		[Tooltip("The steepest slope loose material here stands at, in degrees. Steeper ground sheds down to it.")]
		[Range(15f, 60f)] public float TalusAngleDegrees;

		[Tooltip("How deep the loose soil, sand or regolith lies over the rock on gentle ground, in metres. It washes away far more easily than rock, and steep ground holds less of it.")]
		[Range(0f, 5f)] public float SoilDepthMetres;

		[Tooltip("How fast slopes soften by creep — frost heave, soil slipping, burrowing — against temperate soil (1).")]
		[Range(0f, 3f)] public float SoilCreep;

		[Tooltip("How much ground must drain to a point before a channel starts there, against temperate ground (1). Low cuts dense gullies (badlands); high leaves broad slopes (karst, wetlands).")]
		[Range(0.25f, 4f)] public float ChannelThreshold;

		[Tooltip("Share of hollows that stay open, their water draining underground instead of filling them to the brim: 0 every hollow fills and spills … 1 sinkholes (karst).")]
		[Range(0f, 1f)] public float DepressionKeeping;

		[Tooltip("How readily a hollow holds standing water rather than drying out or soaking away: 0 never … 1 every hollow is a lake or pond.")]
		[Range(0f, 1f)] public float LakeRetention;

		[Tooltip("How much loose sand lies here for the wind to build into dunes: 0 none … 1 a sand sea.")]
		[Range(0f, 1f)] public float DuneSand;

		[Tooltip("Share of flat-lying rock beds that wear back into benches, mesas and canyons: 0 never … 1 always, where the beds lie flat enough.")]
		[Range(0f, 1f)] public float PlateauShare;

		/// <summary>Temperate soil on moderate rock: the reference every value is measured against, and what a biome with no profile wears like.</summary>
		public static TerrainProcess Temperate => new TerrainProcess
		{
			Erodibility = 1f,
			VegetationCohesion = 0.5f,
			Storminess = 1f,
			TalusAngleDegrees = 34f,
			SoilDepthMetres = 1.5f,
			SoilCreep = 1f,
			ChannelThreshold = 1f,
			DepressionKeeping = 0f,
			LakeRetention = 0.6f,
			PlateauShare = 0f,
		};

		/// <summary>Zero in every field: what weighted sums start from.</summary>
		public static TerrainProcess Zero => default;

		/// <summary>Adds <paramref name="other"/> times <paramref name="weight"/> to this, field by field.</summary>
		public void Accumulate(in TerrainProcess other, float weight)
		{
			Erodibility += other.Erodibility * weight;
			VegetationCohesion += other.VegetationCohesion * weight;
			Storminess += other.Storminess * weight;
			TalusAngleDegrees += other.TalusAngleDegrees * weight;
			SoilDepthMetres += other.SoilDepthMetres * weight;
			SoilCreep += other.SoilCreep * weight;
			ChannelThreshold += other.ChannelThreshold * weight;
			DepressionKeeping += other.DepressionKeeping * weight;
			LakeRetention += other.LakeRetention * weight;
			PlateauShare += other.PlateauShare * weight;
			DuneSand += other.DuneSand * weight;
		}

		/// <summary>Every field multiplied by <paramref name="factor"/>: a weighted sum divided by its total weight.</summary>
		public TerrainProcess Scaled(float factor)
		{
			TerrainProcess result = Zero;
			result.Accumulate(this, factor);
			return result;
		}

		/// <summary>True when every field equals <paramref name="other"/>'s exactly.</summary>
		public bool SameAs(in TerrainProcess other)
		{
			return Erodibility == other.Erodibility
				&& VegetationCohesion == other.VegetationCohesion
				&& Storminess == other.Storminess
				&& TalusAngleDegrees == other.TalusAngleDegrees
				&& SoilDepthMetres == other.SoilDepthMetres
				&& SoilCreep == other.SoilCreep
				&& ChannelThreshold == other.ChannelThreshold
				&& DepressionKeeping == other.DepressionKeeping
				&& LakeRetention == other.LakeRetention
				&& PlateauShare == other.PlateauShare
				&& DuneSand == other.DuneSand;
		}
	}

	/// <summary>
	/// A named way of wearing — Temperate, Arid, Badlands, Karst … — shared by every biome that
	/// wears that way, so tuning deserts is one asset and not a dozen.
	/// </summary>
	/// <remarks>
	/// Read only by the scene generator, in the editor. It rides into builds with the biomes that
	/// reference it, as a few dozen bytes; nothing at runtime reads it.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Terrain Process", menuName = "FishMMO/Biomes/Terrain Process", order = 2)]
	public class TerrainProcessProfile : ScriptableObject
	{
		[TextArea(2, 4)]
		public string Description;

		public TerrainProcess Values = TerrainProcess.Temperate;
	}
}
