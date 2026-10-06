#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How many boulders a river carries and how big.</summary>
	public sealed class RiverBoulderSettings
	{
		/// <summary>
		/// The largest grain the water can move (<see cref="RiverShaping.Competence"/>), metres, under which
		/// it brings nothing bigger than cobbles: no boulders. At <see cref="FullGrainMetres"/> they are as
		/// thick as they come.
		/// </summary>
		public float MinGrainMetres = 0.1f;
		public float FullGrainMetres = 0.5f;
		/// <summary>Boulders per 100 m of river at <see cref="FullGrainMetres"/>.</summary>
		public float PerHundredMetres = 8f;
		/// <summary>How many times as many in a rapid, and in a fall.</summary>
		public float RapidFactor = 2f;
		public float FallFactor = 3f;
		/// <summary>Share of boulders on the banks rather than in the channel.</summary>
		public float BankShare = 0.25f;
		/// <summary>The smallest and largest boulder, metres across.</summary>
		public float MinSize = 0.5f;
		public float MaxSize = 2.6f;
		/// <summary>Share of a boulder's height buried in the bed, the least and the most.</summary>
		public float MinBuried = 0.25f;
		public float MaxBuried = 0.5f;
		/// <summary>The most boulders one scene takes.</summary>
		public int MaxBoulders = 4000;
	}

	/// <summary>One boulder: where, how big, which way it lies, and its rock.</summary>
	public struct RiverBoulder
	{
		public Vector3 Position;
		public float Size;
		public Quaternion Rotation;
		public string Prefab;
	}

	/// <summary>
	/// Boulders in a scene's rivers: in fast water and rapids, as big as the current could move, worn round,
	/// half sunk in the bed, in the rock the ground is made of.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where the water can move them.</b> A river drops what it can no longer move, so boulders lie where
	/// the force of its flow on its bed (Shields' criterion, <see cref="RiverShaping.Competence"/>) moves
	/// stones of <see cref="RiverBoulderSettings.MinGrainMetres"/> or more, and nowhere gentler; more the
	/// stronger, twice as many in rapids and three times under falls. Size follows the same competence,
	/// capped by the channel.
	/// </para>
	/// <para>
	/// <b>Each is recorded</b> in the scene's water (<see cref="SceneWater.Boulders"/>), so the flow worked
	/// out later runs round them. They are the existing boulder prefabs, each with its own collider, so
	/// a player wades round them as the water does.
	/// </para>
	/// </remarks>
	public static class RiverBoulders
	{
		public const string RootName = "River Boulders";
		public const string PrefabFolder = "Assets/Prefabs/Shared/Biomes/Generated/Prefabs";

		/// <summary>Where the boulders go. Deterministic in the seed.</summary>
		/// <param name="ground">The finished ground at (east, north), scene metres.</param>
		/// <param name="rockTypeAt">The rock at (east, altitude, north), a geology name or null.</param>
		public static List<RiverBoulder> Plan(SceneWater water, Func<float, float, float> ground, Func<float, float, float, string> rockTypeAt,
			uint seed, RiverBoulderSettings settings = null)
		{
			settings ??= new RiverBoulderSettings();
			var result = new List<RiverBoulder>();
			foreach (RiverPath river in water.Rivers)
			{
				if (!river.Perennial || river.Count < 2)
				{
					continue;
				}
				var random = new System.Random(unchecked((int)(seed ^ (uint)(river.Id * 0x9E3779B1u) ^ 0xB0D1DE5u)));
				RiverShaping.Normals(river.X, river.Z, out float[] nx, out float[] nz);
				double owed = random.NextDouble();
				for (int i = 1; i < river.Count && result.Count < settings.MaxBoulders; i++)
				{
					// Competence, not speed: what the flow's force on its bed can move (Shields).
					float grain = river.Grain != null ? river.Grain[i] : 0f;
					float quick = grain <= settings.MinGrainMetres ? 0f
						: Mathf.Clamp01(Mathf.Log(grain / settings.MinGrainMetres) / Mathf.Log(settings.FullGrainMetres / settings.MinGrainMetres));
					RiverReach reach = river.Reach != null ? river.Reach[i] : RiverReach.Run;
					float factor = reach == RiverReach.Fall ? settings.FallFactor : reach == RiverReach.Rapid ? settings.RapidFactor : 1f;
					if (quick <= 0f && reach < RiverReach.Rapid)
					{
						continue;
					}
					float density = settings.PerHundredMetres / 100f * Mathf.Max(quick, reach >= RiverReach.Rapid ? 0.35f : 0f) * factor;
					owed += density * (river.S[i] - river.S[i - 1]);
					while (owed >= 1.0 && result.Count < settings.MaxBoulders)
					{
						owed -= 1.0;
						float half = 0.5f * river.Width[i];
						bool onBank = random.NextDouble() < settings.BankShare;
						float across = onBank
							? (random.NextDouble() < 0.5 ? -1f : 1f) * (half + (float)random.NextDouble() * Mathf.Max(1f, 0.4f * half))
							: ((float)random.NextDouble() * 2f - 1f) * 0.85f * half;
						float along = (float)(random.NextDouble() - 0.5) * (river.S[i] - river.S[i - 1]);
						float tx = river.X[i] - river.X[i - 1], tz = river.Z[i] - river.Z[i - 1];
						float tl = Mathf.Max(1e-4f, Mathf.Sqrt(tx * tx + tz * tz));
						float x = river.X[i] + nx[i] * across + tx / tl * along;
						float z = river.Z[i] + nz[i] * across + tz / tl * along;
						/* As big as the water can move and up to a few times bigger: the lag it left behind, too heavy for it
						 * now. Never bigger than the channel holds. */
						float size = Mathf.Clamp(Mathf.Max(grain, settings.MinSize * 0.5f) * Mathf.Lerp(1.5f, 4f, (float)random.NextDouble()), settings.MinSize, settings.MaxSize);
						size = Mathf.Clamp(size, settings.MinSize, Mathf.Max(settings.MinSize, 0.6f * river.Width[i]));
						float buried = Mathf.Lerp(settings.MinBuried, settings.MaxBuried, (float)random.NextDouble());
						// Its foot: sunk by its buried share into the bed.
						float y = ground(x, z) - buried * size;
						double shapeRoll = random.NextDouble();
						// Worn round in the water; slabs and the odd spire where it has not had them long.
						string shape = shapeRoll < 0.7 ? "Round" : shapeRoll < 0.95 ? "Slab" : "Spire";
						string rock = Material(rockTypeAt?.Invoke(x, y, z));
						Quaternion rotation = Quaternion.Euler((float)(random.NextDouble() - 0.5) * 20f, (float)random.NextDouble() * 360f, (float)(random.NextDouble() - 0.5) * 20f);
						result.Add(new RiverBoulder { Position = new Vector3(x, y, z), Size = size, Rotation = rotation, Prefab = $"Boulder_{rock}_{shape}" });
					}
				}
			}
			return result;
		}

		/// <summary>The boulder art for a geology rock name.</summary>
		public static string Material(string rockType)
		{
			switch (rockType)
			{
				case "Basalt":
				case "Andesite":
				case "Tuff":
					return "Basalt";
				case "Sandstone":
				case "Conglomerate":
					return "Sandstone";
				case "Limestone":
				case "Chalk":
				case "Marble":
					return "Limestone";
				default:
					return "Grey";
			}
		}

		/// <summary>
		/// Places the boulders in a scene under one root, replacing any it placed before, and records each
		/// in the water (position and radius) for the flow to run round. Returns how many were placed.
		/// </summary>
		/// <summary>The source the boulders' props and colliders are baked under (<see cref="ScenePropBaker"/>).</summary>
		public const string PropSource = "Boulders";

		public static int Place(Scene scene, SceneWater water, List<RiverBoulder> boulders, List<string> notes)
		{
			foreach (GameObject old in scene.GetRootGameObjects())
			{
				if (old.name == RootName)
				{
					UnityEngine.Object.DestroyImmediate(old);
				}
			}
			water.Boulders.Clear();
			/* Baked props, not prefab instances: the "Boulders" set drawn instanced by the client, their colliders
			 * streamed near characters (ScenePropBaker). */
			var prototypes = new List<ScenePropSet.Prototype>();
			var prototypeOf = new Dictionary<GameObject, int>();
			var props = new List<ScenePropSet.Prop>(boulders.Count);
			var prefabs = new Dictionary<string, (GameObject prefab, float size, float baseOffset)>();
			int placed = 0, missing = 0;
			foreach (RiverBoulder boulder in boulders)
			{
				if (!prefabs.TryGetValue(boulder.Prefab, out var art))
				{
					var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{boulder.Prefab}.prefab");
					art = (prefab, 1f, 0f);
					if (prefab != null)
					{
						// Its own size, from what it draws: the prefab is scaled to the planned size from this.
						Bounds bounds = default;
						bool any = false;
						foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
						{
							if (filter.sharedMesh == null)
							{
								continue;
							}
							Bounds b = filter.sharedMesh.bounds;
							if (!any) { bounds = b; any = true; } else { bounds.Encapsulate(b); }
						}
						art.size = any ? Mathf.Max(0.1f, Mathf.Max(bounds.size.x, bounds.size.z)) : 1f;
						art.baseOffset = any ? bounds.min.y : 0f;
					}
					prefabs[boulder.Prefab] = art;
				}
				if (art.prefab == null)
				{
					missing++;
					continue;
				}
				float scale = boulder.Size / art.size;
				if (!prototypeOf.TryGetValue(art.prefab, out int prototype))
				{
					prototype = prototypes.Count;
					prototypeOf[art.prefab] = prototype;
					prototypes.Add(new ScenePropSet.Prototype { Prefab = art.prefab, Layer = -1 });
				}
				// The planned position is where its foot goes: buried by the planned share.
				Vector3 at = boulder.Position - Vector3.up * art.baseOffset * scale;
				var prop = new ScenePropSet.Prop { Prototype = prototype, Position = at, Rotation = boulder.Rotation, Scale = Vector3.one * scale };
				props.Add(prop);
				water.Boulders.Add(new Vector4(boulder.Position.x, boulder.Position.y + 0.5f * boulder.Size, boulder.Position.z, 0.5f * boulder.Size));
				placed++;
			}
			int collidable = ScenePropBaker.Write(scene, PropSource, prototypes, props);
			notes?.Add($"River boulders: {placed:N0} placed in fast water and rapids as baked props, {collidable:N0} collidable{(missing > 0 ? $"; {missing:N0} skipped, their boulder art is missing (run Generate biome art)" : string.Empty)}.");
			return placed;
		}
	}
}
#endif
