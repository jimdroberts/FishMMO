#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The run-time cave cover's art (<see cref="CaveCoverMeshes"/>, 2026-10-20): one payload mesh, <c>CaveCover_Set</c>, every
	/// cover type at every level of detail a submesh of it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Built with the structure kit</b>, after the cave glow art (BiomeArtGenerator.Structures.cs), so "Generate biome art", the
	/// structures-only run and the Structures family's fingerprint all cover it; its path is listed with the structures'
	/// (ProceduralArtCatalogue.CaveCoverPayloadPaths).
	/// </para>
	/// <para>
	/// <b>No prefab and no material.</b> Nothing places the cover: the client's cover renderer (FishMMO.Client CaveCoverSystem)
	/// scatters it on the cave rock every frame and draws the submeshes itself with its own shader (FishMMO/Cave Cover), which
	/// colours them from their vertex colours and the cave's glow palette. The Weather Render Profile references the mesh so a
	/// client build carries it (in the editor the renderer loads it by path when the profile does not).
	/// </para>
	/// </remarks>
	public static partial class BiomeArtGenerator
	{
		/// <summary>The steps the cave cover adds to the structures': the one mesh.</summary>
		private static int CaveCoverStepCount() => 1;

		/// <summary>The cover's mesh: after the cave glow art.</summary>
		private static IEnumerable<string> CaveCoverSteps(Context c)
		{
			yield return "Structure: cave cover";
			WriteCaveCover(c);
		}

		private static void WriteCaveCover(Context c)
		{
			MeshBuilder set = CaveCoverMeshes.BuildSet(c.Seed);
			Mesh mesh = WriteMesh(c, set, CaveCoverMeshes.SetMeshName, false);
			if (mesh != null && mesh.subMeshCount != CaveCoverTypes.Count * CaveCoverTypes.Levels)
			{
				c.Report.Problems.Add($"{CaveCoverMeshes.SetMeshName}: {mesh.subMeshCount} submeshes, {CaveCoverTypes.Count * CaveCoverTypes.Levels} expected.");
			}
		}
	}

	public static partial class ProceduralArtCatalogue
	{
		/// <summary>The cave cover's payload: its one mesh (BiomeArtGenerator.CaveCover.cs).</summary>
		public static IEnumerable<string> CaveCoverPayloadPaths()
		{
			yield return MeshPath(CaveCoverMeshes.SetMeshName);
		}

		/// <summary>The project path of the cave cover's mesh (the client's cover renderer loads it from here in the editor).</summary>
		public static string CaveCoverMeshPath => MeshPath(CaveCoverMeshes.SetMeshName);
	}
}
#endif
