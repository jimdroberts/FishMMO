using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Measures, rather than reads from YAML, that every terrain scene's baked NavMesh sits on its
	/// terrain.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the property issue #220 actually rests on. The NavMeshAgent no longer drives the
	/// transform; <c>AIController.StepAgent</c> sets the transform to the agent's NavMesh position
	/// every tick, so an NPC's y IS the mesh's y at that column — a mesh that floats above the
	/// terrain is an NPC that floats, and one below it is an NPC in the ground.
	/// <see cref="NavMeshSceneBakeTests"/> pins the bake <em>flags</em>; this pins the bake
	/// <em>result</em>, which is what a terrain edit without a rebake breaks while every flag
	/// still reads correctly.
	/// </para>
	/// <para>
	/// Baseline after the 2026-09-03 rebake: mean absolute error 0.000 m on all four scenes, one
	/// 0.43 m outlier at a single Dungeon column. Before it, three scenes averaged 0.10–0.19 m
	/// off with a quarter of their columns beyond 0.25 m. The limits sit well clear of the good
	/// bake and well inside the bad one.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NavMeshTerrainAgreementTests
	{
		private const string SceneRoot = "Assets/Scenes/WorldScene";

		/// <summary>Columns sampled per terrain axis.</summary>
		private const int GridSamples = 48;

		/// <summary>Reach of the NavMesh sample around the terrain height.</summary>
		private const float SampleRadius = 0.75f;

		/// <summary>
		/// A hit further than this from the column, horizontally, is the nearest mesh being
		/// somewhere else — a ledge beside a cliff column — not the mesh floating over this one.
		/// Only near-vertical hits measure what an NPC standing here would feel.
		/// </summary>
		private const float MaxHorizontalOffset = 0.1f;

		/// <summary>How many of the worst columns to name in the output.</summary>
		private const int WorstToReport = 3;

		/// <summary>Largest acceptable mean |navmesh y − terrain y| per scene, in metres.</summary>
		private const float MeanAbsLimit = 0.03f;

		/// <summary>A column this far off is a visible float or sink.</summary>
		private const float FarOffMetres = 0.25f;

		/// <summary>Largest acceptable share of sampled columns beyond <see cref="FarOffMetres"/>.</summary>
		private const float FarOffShareLimit = 0.01f;

		private struct Agreement
		{
			public string Scene;
			public int Columns;
			public int Sampled;
			public float MeanSigned;
			public float MeanAbs;
			public float P95Abs;
			public float MaxAbs;
			public int FarOff;
			public int Sideways;
			public List<string> Worst;
		}

		private static List<string> TerrainScenePaths()
		{
			List<string> paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneRoot }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".unity"))
				{
					continue;
				}
				string text = File.ReadAllText(path);
				bool hasTerrain = Regex.IsMatch(text, @"^Terrain:\s*$", RegexOptions.Multiline);
				bool hasSurfaceData = Regex.IsMatch(text, @"^\s+m_NavMeshData:\s*\{fileID:\s*\d+,\s*guid:", RegexOptions.Multiline);
				if (hasTerrain && hasSurfaceData)
				{
					paths.Add(path);
				}
			}
			paths.Sort();
			return paths;
		}

		[Test]
		public void EveryTerrainScene_NavMeshSitsOnItsTerrain()
		{
			List<string> scenes = TerrainScenePaths();
			Assert.That(scenes, Is.Not.Empty, "no terrain scene with a baked NavMeshSurface — the pin would be vacuous");

			SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
			List<string> failures = new List<string>();
			List<Agreement> report = new List<Agreement>();
			try
			{
				foreach (string path in scenes)
				{
					// Opening the scene enables its NavMeshSurface, which adds the baked data to the
					// global NavMesh exactly as a scene server does.
					EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					Terrain[] terrains = Terrain.activeTerrains;
					Assert.That(terrains, Is.Not.Empty, path);

					Agreement agreement = Measure(path, terrains);
					report.Add(agreement);

					if (agreement.Sampled == 0)
					{
						failures.Add($"{path}: no NavMesh within {SampleRadius} m of the terrain at any of {agreement.Columns} columns — is the surface's baked data loading?");
						continue;
					}
					float farOffShare = (float)agreement.FarOff / agreement.Sampled;
					if (agreement.MeanAbs > MeanAbsLimit || farOffShare > FarOffShareLimit)
					{
						failures.Add($"{path}: NavMesh is off the terrain (mean |dy| {agreement.MeanAbs:0.000} m, {farOffShare:P1} of columns beyond {FarOffMetres} m). Rebake with Build Height Mesh on.");
					}
				}
			}
			finally
			{
				if (previous != null && previous.Length > 0)
				{
					EditorSceneManager.RestoreSceneManagerSetup(previous);
				}
				else
				{
					EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
				}
			}

			foreach (Agreement a in report)
			{
				TestContext.WriteLine($"{Path.GetFileNameWithoutExtension(a.Scene)}: sampled {a.Sampled}/{a.Columns} columns ({a.Sideways} sideways hits ignored), mean dy {a.MeanSigned:+0.000;-0.000} m, mean |dy| {a.MeanAbs:0.000} m, p95 |dy| {a.P95Abs:0.000} m, max |dy| {a.MaxAbs:0.000} m, {a.FarOff} beyond {FarOffMetres} m; worst: {string.Join(", ", a.Worst)}");
			}

			Assert.That(failures, Is.Empty, string.Join("\n", failures));
		}

		private static Agreement Measure(string scenePath, Terrain[] terrains)
		{
			List<float> errors = new List<float>();
			List<KeyValuePair<float, string>> worst = new List<KeyValuePair<float, string>>();
			int columns = 0;
			int sideways = 0;
			float signedSum = 0f;
			int farOff = 0;

			foreach (Terrain terrain in terrains)
			{
				if (terrain == null || terrain.terrainData == null)
				{
					continue;
				}
				Vector3 origin = terrain.transform.position;
				Vector3 size = terrain.terrainData.size;

				for (int ix = 0; ix < GridSamples; ++ix)
				{
					for (int iz = 0; iz < GridSamples; ++iz)
					{
						columns++;
						// Half a cell in from every edge so the sample never straddles the terrain border.
						float x = origin.x + size.x * (ix + 0.5f) / GridSamples;
						float z = origin.z + size.z * (iz + 0.5f) / GridSamples;
						Vector3 ground = new Vector3(x, 0f, z);
						ground.y = terrain.SampleHeight(ground) + origin.y;

						if (!NavMesh.SamplePosition(ground, out NavMeshHit hit, SampleRadius, NavMesh.AllAreas))
						{
							// Too steep, or outside the surface — nothing stands here.
							continue;
						}

						Vector3 lateral = hit.position - ground;
						lateral.y = 0f;
						if (lateral.sqrMagnitude > MaxHorizontalOffset * MaxHorizontalOffset)
						{
							sideways++;
							continue;
						}

						float dy = hit.position.y - ground.y;
						signedSum += dy;
						worst.Add(new KeyValuePair<float, string>(Mathf.Abs(dy), $"({x:0.0}, {z:0.0}) dy {dy:+0.00;-0.00}"));
						errors.Add(Mathf.Abs(dy));
						if (Mathf.Abs(dy) > FarOffMetres)
						{
							farOff++;
						}
					}
				}
			}

			worst.Sort((a, b) => b.Key.CompareTo(a.Key));
			List<string> worstNames = new List<string>();
			for (int i = 0; i < worst.Count && i < WorstToReport; ++i)
			{
				worstNames.Add(worst[i].Value);
			}

			Agreement result = new Agreement { Scene = scenePath, Columns = columns, Sampled = errors.Count, FarOff = farOff, Sideways = sideways, Worst = worstNames };
			if (errors.Count == 0)
			{
				return result;
			}

			errors.Sort();
			float absSum = 0f;
			foreach (float e in errors)
			{
				absSum += e;
			}
			result.MeanSigned = signedSum / errors.Count;
			result.MeanAbs = absSum / errors.Count;
			result.P95Abs = errors[Mathf.Clamp(Mathf.FloorToInt(errors.Count * 0.95f), 0, errors.Count - 1)];
			result.MaxAbs = errors[errors.Count - 1];
			return result;
		}
	}
}
