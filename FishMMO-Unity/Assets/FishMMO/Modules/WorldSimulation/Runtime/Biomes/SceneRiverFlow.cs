using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The solved flow down a scene's rivers: per river, a field over its line (metres along it by cells from bank to
	/// bank) holding the current's along and across components over the section's mean speed, so 1 along is the
	/// river's own mean speed there. Eddies behind its boulders, the fast core and the slack water at its banks are in
	/// it; what the water's ripples and foam ride on the client.
	/// </summary>
	/// <remarks>
	/// A sub-asset of the scene's <see cref="SceneBiomeMap"/>, reached through its <see cref="SceneBiomeMap.RiverFlow"/>
	/// reference: clients load it when a scene's water is built, and the server, which loads the map for the weather,
	/// never does. Solved by the scene generator (RiverFlowSolver), never at run time.
	/// </remarks>
	public class SceneRiverFlow : ScriptableObject
	{
		/// <summary>One river's field.</summary>
		[Serializable]
		public class River
		{
			/// <summary>The river's id in the scene's <see cref="SceneHydrology"/>.</summary>
			public int Id;

			/// <summary>Cells along it, and the metres each covers.</summary>
			public int Length;
			public float AlongMetres = 1f;

			/// <summary>Length × cells across: R the current along the river, G across it (toward its left bank), over the section's mean speed.</summary>
			public Texture2D Field;
		}

		public List<River> Rivers = new List<River>();

		/// <summary>The field of river <paramref name="id"/>, or null.</summary>
		public River Find(int id) => Rivers.Find(r => r != null && r.Id == id);
	}
}
