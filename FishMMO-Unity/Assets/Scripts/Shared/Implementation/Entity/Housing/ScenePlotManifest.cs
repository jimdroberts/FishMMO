using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The housing lots a scene cut laid, kept beside the scene's terrain.
	/// </summary>
	/// <remarks>
	/// A plot's database row is keyed by its scene and its key, and a key is only worth anything if
	/// the land it names stays where it was. A re-cut builds the whole scene again from the planet,
	/// so anything that changed upstream — a template, an erosion setting, the generator itself —
	/// could move every lot. The cut reads this before it clears the old scene and lays every lot in
	/// it again exactly where it stood, at the same floor height, before it looks for room for new
	/// ones; owned land never moves.
	/// </remarks>
	public class ScenePlotManifest : ScriptableObject
	{
		/// <summary>Bumped when <see cref="Lot"/> changes meaning.</summary>
		public const int CurrentFormat = 1;

		/// <summary>One laid lot.</summary>
		[Serializable]
		public struct Lot
		{
			/// <summary>The plot key written on the foundation (canonical form).</summary>
			public string Key;

			/// <summary>The middle of the lot: x and z in scene metres, y the levelled floor.</summary>
			public Vector3 Centre;

			/// <summary>Footprint: width (X) by depth (Z), metres.</summary>
			public Vector2 Size;

			/// <summary>Which edge the deed post stands on: 0 +X, 1 +Z, 2 −X, 3 −Z.</summary>
			public int Front;

			/// <summary>What claiming the lot costs.</summary>
			public long Price;

			/// <summary>True for a lot in a district beside a settlement, false for countryside.</summary>
			public bool District;

			/// <summary>The group the lot was laid with (a district's settlement, or a countryside cell), for the hierarchy.</summary>
			public string Group;
		}

		/// <summary>The format this asset was written in.</summary>
		public int Format = CurrentFormat;

		/// <summary>Every lot, in the order laid.</summary>
		public List<Lot> Lots = new List<Lot>();

		/// <summary>The manifest's path in a scene's terrain folder.</summary>
		public static string AssetPath(string terrainFolder, string sanitizedSceneName)
			=> $"{terrainFolder}/{sanitizedSceneName}_HousingPlots.asset";
	}
}
