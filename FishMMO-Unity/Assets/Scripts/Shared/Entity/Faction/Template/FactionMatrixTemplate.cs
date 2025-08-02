using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Faction Matrix", menuName = "FishMMO/Character/Faction/Faction Matrix", order = 1)]
	public class FactionMatrixTemplate : CachedScriptableObject<FactionMatrixTemplate>, ICachedObject
	{
		/// <summary>
		/// List of all faction templates included in this matrix.
		/// Used to define the set of factions and their relationships.
		/// </summary>
		public List<FactionTemplate> Factions;

		/// <summary>
		/// The matrix of alliance levels between all factions in <see cref="Factions"/>.
		/// </summary>
		public FactionMatrix Matrix;

		/// <summary>
		/// The display name of this faction matrix (from the ScriptableObject's name).
		/// </summary>
		public string Name { get { return this.name; } }

#if UNITY_EDITOR
		/// <summary>
		/// Rebuilds the faction matrix by loading all FactionTemplate assets using Addressables.
		/// Initializes the <see cref="Factions"/> list and creates a new <see cref="Matrix"/> with default relationships.
		/// </summary>
		public void RebuildMatrix()
		{
			Factions = new List<FactionTemplate>();

			// Load all FactionTemplate assets synchronously using Addressables
			var handle = Addressables.LoadAssetsAsync<FactionTemplate>("FactionTemplate");

			// Block until the assets are loaded synchronously
			var result = handle.WaitForCompletion();

			if (result != null && result.Count > 0)
			{
				Factions.AddRange(result);
				if (Factions.Count < 1)
				{
					return;
				}

				// Create a new matrix with all relationships set to Neutral
				Matrix = new FactionMatrix(Factions);
			}
			else
			{
				Log.Error("FactionMatrixTemplate", "Failed to load Faction Templates with Addressables.");
			}
		}

		/// <summary>
		/// Rebuilds the default relationships (allied, neutral, hostile) for each faction based on the matrix values.
		/// Clears existing relationships and sets new ones according to the alliance level in the matrix.
		/// </summary>
		public void RebuildFactions()
		{
			if (Factions == null ||
				Factions.Count < 1)
			{
				return;
			}
			if (Matrix == null ||
				Matrix.Factions == null ||
				Matrix.Factions.Length < 1)
			{
				return;
			}

			// Clear all default relationships for each faction
			for (int i = 0; i < Factions.Count; ++i)
			{
				Factions[i].DefaultAllied.Clear();
				Factions[i].DefaultNeutral.Clear();
				Factions[i].DefaultHostile.Clear();
			}

			// Set up relationships based on matrix values
			for (int y = 0; y < Factions.Count; ++y)
			{
				for (int x = 0; x < Factions.Count; ++x)
				{
					int index = x + y * Factions.Count;

					// Same faction is always allied
					if (x == y)
					{
						Factions[x].DefaultAllied.Add(Factions[y]);
						UnityEditor.EditorUtility.SetDirty(Factions[x]);
					}
					else
					{
						FactionAllianceLevel allianceLevel = Matrix.Factions[index];
						if (allianceLevel == FactionAllianceLevel.Neutral)
						{
							Factions[x].DefaultNeutral.Add(Factions[y]);
							UnityEditor.EditorUtility.SetDirty(Factions[x]);
						}
						else
						{
							Factions[x].DefaultHostile.Add(Factions[y]);
							UnityEditor.EditorUtility.SetDirty(Factions[x]);
						}
					}
				}
			}
		}
#endif
	}
}