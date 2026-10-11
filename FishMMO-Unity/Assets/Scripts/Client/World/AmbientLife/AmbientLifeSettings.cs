using System;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// The land's ambient life (<see cref="AmbientLifeSystem"/>): how far each family is drawn, how many there are,
	/// and the budget. On the Weather Render Profile ("Ambient life"); read every frame, so changes apply while
	/// playing. The kinds themselves (what lives where, how it moves) are code (<see cref="AmbientLifeCatalogue"/>),
	/// so a tuning pass never has to fight numbers frozen into the profile asset.
	/// </summary>
	[Serializable]
	public class AmbientLifeSettings
	{
		[Tooltip("Off: no birds, bats or small animals at all.")]
		public bool Enabled = true;
		[Tooltip("The most animals drawn at once; the nearest groups are drawn first.")]
		[Min(0)] public int MaxVisible = 400;
		[Tooltip("The placement grid, metres: each cell decides its own animals from its coordinates, so a cell's life is the same whenever and wherever it is first seen.")]
		[Min(16f)] public float CellMetres = 64f;
		[Tooltip("Animals take fright and flee from the player, the camera and other characters coming close.")]
		public bool Flush = true;

		[Header("Draw distance by family, metres")]
		[Tooltip("Flocks wheeling over open country and the coast.")]
		[Min(10f)] public float FlockDistance = 260f;
		[Tooltip("Birds alone or in pairs, perched, foraging, or on the water.")]
		[Min(10f)] public float SoloBirdDistance = 140f;
		[Tooltip("Raptors circling high in thermals.")]
		[Min(10f)] public float RaptorDistance = 450f;
		[Min(10f)] public float BatDistance = 70f;
		[Tooltip("Rats, mice, squirrels, rabbits, lizards, crabs and frogs: tiny past this.")]
		[Min(5f)] public float CritterDistance = 60f;
		[Tooltip("The last share of each draw distance over which animals dissolve.")]
		[Range(0.05f, 0.5f)] public float FadeBand = 0.2f;
		[Tooltip("Metres from the camera an animal casts a shadow to (kinds that cast shadows).")]
		[Min(0f)] public float ShadowDistance = 30f;

		[Header("Density by family (1 = the catalogue's)")]
		[Min(0f)] public float FlockDensity = 1f;
		[Min(0f)] public float SoloBirdDensity = 1f;
		[Min(0f)] public float RaptorDensity = 1f;
		[Min(0f)] public float BatDensity = 1f;
		[Min(0f)] public float CritterDensity = 1f;

		[Header("Size")]
		[Tooltip("Every animal drawn this many times its real size, so small birds and critters read at play distances (Jim, 2026-10-10: 30-50% bigger). 1 is life size.")]
		[Range(1f, 3f)] public float SizeScale = 1.4f;

		[Header("Debug")]
		[Tooltip("Draw every animal magenta and DebugScale times its size, and log to the console every few seconds how many are drawn, how many are in the camera's view, how many above or behind it, and how far the visible ones are. For finding animals, not for play.")]
		public bool DebugHighlight;
		[Range(1f, 10f)] public float DebugScale = 4f;

		/// <summary>A family's draw distance.</summary>
		public float DistanceOf(AmbientFamily family)
		{
			switch (family)
			{
				case AmbientFamily.Flocks: return FlockDistance;
				case AmbientFamily.SoloBirds: return SoloBirdDistance;
				case AmbientFamily.Raptors: return RaptorDistance;
				case AmbientFamily.Bats: return BatDistance;
				default: return CritterDistance;
			}
		}

		/// <summary>A family's density multiplier.</summary>
		public float DensityOf(AmbientFamily family)
		{
			switch (family)
			{
				case AmbientFamily.Flocks: return FlockDensity;
				case AmbientFamily.SoloBirds: return SoloBirdDensity;
				case AmbientFamily.Raptors: return RaptorDensity;
				case AmbientFamily.Bats: return BatDensity;
				default: return CritterDensity;
			}
		}

		/// <summary>True for the families placed out to the far draw distances (flocks and raptors), placed in a second, wider pass.</summary>
		public static bool IsFar(AmbientFamily family) => family == AmbientFamily.Flocks || family == AmbientFamily.Raptors;
	}
}
