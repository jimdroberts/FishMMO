using System;
using System.Collections.Generic;
using FishMMO.Shared.NameGeneration;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>How many placed points of interest a scene gets, before per-kind overrides.</summary>
	public enum PointOfInterestDensity : byte
	{
		/// <summary>Only the detected kinds (falls, lakes, peaks): nothing placed, nothing built.</summary>
		NaturalOnly,
		Sparse,
		Normal,
		Dense,
		/// <summary>Every kind's budget comes from the overrides (unset ones from Normal).</summary>
		Custom,
	}

	/// <summary>One kind's budget, changed from the catalogue's for one scene. Negative = keep the catalogue's.</summary>
	[Serializable]
	public class PointOfInterestKindOverride
	{
		public POIType Kind;
		public bool Enabled = true;
		/// <summary>Sites per km² before the density multiplier.</summary>
		public float PerKm2 = -1f;
		public int Min = -1;
		public int Max = -1;
		/// <summary>Minimum distance between two sites of this kind, metres.</summary>
		public float SpacingMetres = -1f;
	}

	/// <summary>
	/// One scene's point-of-interest choices: asked before every cut (Jim, 2026-10-10) and editable afterwards.
	/// </summary>
	/// <remarks>
	/// Saved beside the scene's atlas entry, not in its terrain folder, so it exists before the first cut and a
	/// re-cut (which clears the terrain folder) keeps it. The atlas entry points at it
	/// (<see cref="WorldAtlasScene.PointsOfInterest"/>), and the scene's <see cref="ScenePointOfInterestSettings"/>
	/// shows it in the inspector.
	/// </remarks>
	public class PointOfInterestSettings : ScriptableObject
	{
		/// <summary>Whether this scene holds its world's capital city.</summary>
		public bool Capital;
		public PointOfInterestDensity Density = PointOfInterestDensity.Normal;
		/// <summary>Scales every placed kind's budget after the density preset.</summary>
		[Range(0f, 4f)]
		public float Multiplier = 1f;
		public List<PointOfInterestKindOverride> Overrides = new List<PointOfInterestKindOverride>();

		/// <summary>The density preset as a budget multiplier.</summary>
		public float DensityScale => Density switch
		{
			PointOfInterestDensity.NaturalOnly => 0f,
			PointOfInterestDensity.Sparse => 0.4f,
			PointOfInterestDensity.Dense => 1.8f,
			_ => 1f,
		};

		/// <summary>The override for a kind, or null.</summary>
		public PointOfInterestKindOverride OverrideFor(POIType kind)
		{
			foreach (PointOfInterestKindOverride entry in Overrides)
			{
				if (entry != null && entry.Kind == kind)
				{
					return entry;
				}
			}
			return null;
		}

		/// <summary>The settings asset path beside an atlas entry's asset path.</summary>
		public static string PathBeside(string atlasEntryPath, string sanitizedSceneName)
		{
			int slash = atlasEntryPath.LastIndexOf('/');
			string folder = slash >= 0 ? atlasEntryPath.Substring(0, slash) : "Assets";
			return $"{folder}/{sanitizedSceneName} POI Settings.asset";
		}
	}
}
