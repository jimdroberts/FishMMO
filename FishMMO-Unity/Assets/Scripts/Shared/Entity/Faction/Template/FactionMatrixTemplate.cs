using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Faction Matrix", menuName = "FishMMO/Character/Faction/Faction Matrix", order = 1)]
	public class FactionMatrixTemplate : CachedScriptableObject<FactionMatrixTemplate>, ICachedObject
	{
		public List<FactionTemplate> Factions;

		public FactionMatrix Matrix;

		public string Name { get { return this.name; } }

#if UNITY_EDITOR
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

				Matrix = new FactionMatrix(Factions);
			}
			else
			{
				Log.Error("FactionMatrixTemplate", "Failed to load Faction Templates with Addressables.");
			}
		}

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

			for (int i = 0; i < Factions.Count; ++i)
			{
				Factions[i].DefaultAllied.Clear();
				Factions[i].DefaultNeutral.Clear();
				Factions[i].DefaultHostile.Clear();
			}

			for (int y = 0; y < Factions.Count; ++y)
			{
				for (int x = 0; x < Factions.Count; ++x)
				{
					int index = x + y * Factions.Count;

					// same faction is always allied
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