using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// What the generator tells code it cannot see (the client's editor assemblies, which this one does not reference): a
	/// scene's terrains are finished, their heights, holes, paint and details as they will ship. Raised at the end of a cut
	/// and of a repaint (<see cref="SceneGenerator.BakePropsAndNavMesh"/>), with the scene's terrain folder set
	/// (<see cref="ScenePropBaker.BakeFolder"/>), so what a handler bakes goes beside the terrain.
	/// </summary>
	public static class SceneTerrainEvents
	{
		/// <summary>The scene, its terrains, and the notes the generation reports.</summary>
		public static event Action<Scene, IReadOnlyList<Terrain>, List<string>> Finished;

		/// <summary>Tells every handler; one that throws is reported in the notes and the log, and the rest still run.</summary>
		public static void RaiseFinished(Scene scene, IReadOnlyList<Terrain> terrains, List<string> notes)
		{
			if (Finished == null)
			{
				return;
			}
			foreach (Delegate handler in Finished.GetInvocationList())
			{
				try
				{
					((Action<Scene, IReadOnlyList<Terrain>, List<string>>)handler)(scene, terrains, notes);
				}
				catch (Exception ex)
				{
					notes?.Add($"{handler.Method.DeclaringType?.Name} failed after the terrain was finished: {ex.Message}");
					Debug.LogException(ex);
				}
			}
		}
	}
}
