using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared
{

	/// <summary>
	/// One species of wildlife: its role and the numbers its brain and body use. Referenced by the species' NPC prefab
	/// (<see cref="NPC.Wildlife"/>), so server and clients read the same values.
	/// </summary>
	/// <remarks>
	/// Wildlife sits outside the faction system: anyone may harm it and it may harm anyone (<see cref="WildlifeRules"/>),
	/// except predators of one pack, which never turn on each other.
	/// </remarks>
	[CreateAssetMenu(fileName = "New Wildlife", menuName = "FishMMO/Character/NPC/Wildlife", order = 0)]
	public class WildlifeTemplate : ScriptableObject, IWildlifeSpecies
	{
		[Tooltip("How this species treats everything around it.")]
		public WildlifeRole Role = WildlifeRole.Prey;

		/// <inheritdoc />
		WildlifeRole IWildlifeSpecies.Role => Role;

		/// <inheritdoc />
		float IWildlifeSpecies.CorpseSeconds => CorpseSeconds;
		int IWildlifeSpecies.GroupMax => GroupMax;
		BiomeHabitat IWildlifeSpecies.Habitats => Habitats;
		WildlifeShelter IWildlifeSpecies.Shelter => Shelter;
		SurfaceMask IWildlifeSpecies.Surfaces => Surfaces;
		float IWildlifeSpecies.WalkSpeed => WalkSpeed;
		float IWildlifeSpecies.RunSpeed => RunSpeed;

		[Header("Movement")]
		[Tooltip("Metres per second while grazing, wandering or returning.")]
		[Min(0.1f)]
		public float WalkSpeed = 1.4f;

		[Tooltip("Metres per second while fleeing, hunting or charging.")]
		[Min(0.1f)]
		public float RunSpeed = 8f;

		[Header("Fleeing (prey)")]
		[Tooltip("Prey: how far it puts each leg of its flight.")]
		[Min(1f)]
		public float FleeLegMetres = 14f;

		[Tooltip("Prey: how far every threat must be before it stops running.")]
		[Min(1f)]
		public float SafeMetres = 40f;

		[Header("Corpse")]
		[Tooltip("Seconds the corpse stays, loot or not, so professions (skinning and the like) can work it.")]
		[Min(0f)]
		public float CorpseSeconds = 300f;

		[Header("Body")]
		[Tooltip("The procedural body every client builds for this species (WildlifeBodyMesh).")]
		public WildlifeBodySpec Body = WildlifeBodySpec.Deer;

		[Tooltip("The wildlife material (FishMMO/Wildlife shader). Shared by every species: colours ride the mesh.")]
		public Material Material;

		[Header("Placement")]
		[Tooltip("Ground it lives on (the biome's habitat). Settlements, sea and keep-outs are never used.")]
		public BiomeHabitat Habitats = BiomeHabitat.Forest | BiomeHabitat.Woodland | BiomeHabitat.Grassland;

		[Tooltip("Out in the open, in caves and mines, or both. Every spawn's ground is checked (WildlifeRules.LivesOn).")]
		public WildlifeShelter Shelter = WildlifeShelter.Open;

		[Tooltip("The grounds (by their painted texture) it may appear on; nothing set for any ground.")]
		public SurfaceMask Surfaces = SurfaceMask.Any;

		[Tooltip("Groups per square kilometre of suitable ground.")]
		[Min(0f)]
		public float GroupsPerKm2 = 0.6f;

		[Tooltip("Fewest animals in a group (a herd or a pack).")]
		[Min(1)]
		public int GroupMin = 2;

		[Tooltip("Most animals in a group.")]
		[Min(1)]
		public int GroupMax = 5;

		[Tooltip("Points of interest a group lives at (a wolf den, a hunting lodge's woods): one group each, bigger than in the open.")]
		public List<POIType> DenKinds = new List<POIType>();

		[Tooltip("True to den one group in some cave mouths (bears).")]
		public bool DensInCaveMouths;

		[Tooltip("True to keep colonies deep inside caves and mines, on their dark floors with room to fly over them (giant bats).")]
		public bool ColoniesInCaves;

		[Tooltip("Seconds before a fallen member is replaced: the shortest.")]
		[Min(0f)]
		public float RespawnMinSeconds = 180f;

		[Tooltip("Seconds before a fallen member is replaced: the longest.")]
		[Min(0f)]
		public float RespawnMaxSeconds = 420f;
	}

	/// <summary>
	/// The shape of a procedural animal, in metres: torso, legs, neck and head, ears, horns and tail, and three colours.
	/// Read by every client to build one mesh per species (<see cref="WildlifeBodyMesh"/>); the server uses it only for the
	/// collider the builder sizes.
	/// </summary>
	[Serializable]
	public struct WildlifeBodySpec
	{
		[Tooltip("Torso length, nose of the chest to the rump.")] public float Length;
		[Tooltip("Torso depth, back to belly.")] public float Depth;
		[Tooltip("Torso width.")] public float Width;
		[Tooltip("Ground to belly.")] public float LegLength;
		[Tooltip("Leg thickness.")] public float LegThickness;
		[Tooltip("Neck length.")] public float NeckLength;
		[Tooltip("Neck angle up from horizontal, degrees.")] public float NeckRaise;
		[Tooltip("Head length (with the muzzle).")] public float HeadLength;
		[Tooltip("Head height and width.")] public float HeadSize;
		[Tooltip("Ear length.")] public float EarSize;
		[Tooltip("Horns: none, antlers (deer), broad antlers (elk), tusks (boar).")] public WildlifeHorns Horns;
		[Tooltip("Horn or antler size.")] public float HornSize;
		[Tooltip("Tail length.")] public float TailLength;
		[Tooltip("Tail angle up from hanging, degrees (a fox's brush is near level).")] public float TailRaise;
		[Tooltip("Wing span tip to tip, metres; 0 for none. Wings take the place of the front legs (a bat).")] public float WingSpan;
		[Tooltip("How high it flies over the ground it stands on, metres; 0 walks. Drawn only: the server moves it on the ground.")] public float HoverHeight;
		[Tooltip("Back and flanks (linear-ish sRGB, as authored).")] public Color Back;
		[Tooltip("Belly, throat and legs' inner side.")] public Color Belly;
		[Tooltip("Muzzle, hooves or paws, horns.")] public Color Accent;

		/// <summary>Height of the back above the ground.</summary>
		public float ShoulderHeight => LegLength + Depth;

		public static WildlifeBodySpec Deer => new WildlifeBodySpec
		{
			Length = 1.25f, Depth = 0.45f, Width = 0.36f, LegLength = 0.62f, LegThickness = 0.07f,
			NeckLength = 0.5f, NeckRaise = 55f, HeadLength = 0.34f, HeadSize = 0.17f, EarSize = 0.14f,
			Horns = WildlifeHorns.Antlers, HornSize = 0.4f, TailLength = 0.12f, TailRaise = 35f,
			Back = new Color(0.52f, 0.36f, 0.22f), Belly = new Color(0.84f, 0.76f, 0.62f), Accent = new Color(0.16f, 0.13f, 0.11f),
		};
	}

	/// <summary>What grows from an animal's head.</summary>
	public enum WildlifeHorns : byte
	{
		None = 0,
		Antlers = 1,
		BroadAntlers = 2,
		Tusks = 3,
	}
}
