#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Bakes each body's climate table into the body's own asset (<see cref="WorldBody.ClimateTable"/>), so a server or a
	/// client reads it at load rather than solving the year's energy balance (<see cref="PlanetClimateTable.Solve"/>), which
	/// takes the better part of a second a body.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A table is written only when it is missing or its fingerprint no longer matches its body — the body's orbit, air,
	/// water, seed, its suns and the model's version (<see cref="PlanetClimateTable.Fingerprint"/>) — so baking again changes
	/// nothing that is current, and a body's asset is touched only when its climate really changed.
	/// </para>
	/// <para>
	/// Run by hand from the dashboard, and on its own wherever a table is about to be relied on: before a globe is baked,
	/// before a scene is cut, and before any build, client or server.
	/// </para>
	/// </remarks>
	public static class ClimateTableBaker
	{
		[DashboardTool(DashboardToolAttribute.Maintenance, "Bake climate tables", Section = "World", Order = 5,
			Tooltip = "Solves each body's climate table where it is missing or out of date and stores it on the body, so the game reads it at load instead of solving it.")]
		public static void BakeAllFromDashboard()
		{
			int baked = BakeAll(out int current);
			Debug.Log($"[Climate] Baked {baked} climate table(s); {current} were already current.");
		}

		/// <summary>Every body's table, where it is missing or stale. Returns how many were written; <paramref name="current"/> how many were not needed.</summary>
		public static int BakeAll(out int current)
		{
			current = 0;
			int baked = 0;
			foreach (WorldBody body in WorldEditorAssets.FindAll<WorldBody>())
			{
				if (body == null)
				{
					continue;
				}
				if (EnsureBaked(body))
				{
					baked++;
				}
				else
				{
					current++;
				}
			}
			if (baked > 0)
			{
				AssetDatabase.SaveAssets();
			}
			return baked;
		}

		/// <summary>
		/// A body's table, baked when it is missing or stale. True when it was written. Does not save the project: the
		/// asset is marked dirty and saved with the next save (or by <see cref="BakeAll"/>).
		/// </summary>
		public static bool EnsureBaked(WorldBody body)
		{
			if (body == null)
			{
				return false;
			}
			SolarSystemProfile system = SolarSystemProfile.Resolve(body);
			long fingerprint = PlanetClimateTable.Fingerprint(system, body);
			if (body.ClimateTable != null && body.ClimateTable.HasTable && body.ClimateTable.Fingerprint == fingerprint)
			{
				return false;
			}
			PlanetClimateTable table = PlanetClimateTable.Solve(system, body);
			body.ClimateTable = table.ToData(fingerprint);
			EditorUtility.SetDirty(body);
			// The next read takes the baked copy, as the game will.
			PlanetClimateTable.ClearCache();
			Debug.Log($"[Climate] Baked {body.name}'s climate table (mean {table.MeanKelvin - 273.15:0.0} °C, residual {table.Residual:+0.00;-0.00} K).");
			return true;
		}
	}
}
#endif
