using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// Walks the world sim walker (the game's own character movement, WorldSimWalker) across a cloud world's rope bridges in
	/// play mode: from the grass a couple of metres inland of one end, along the bridge, onto the grass past the other. A
	/// bridge passes when the walker gets there; it fails if the walker falls (below both ends) or stops (no headway).
	/// What a geometry test cannot say: that the planks, the landings and the rim's stone, as streamed collision, carry a
	/// capsule of the players' size across (2026-10-15: "the bridges… need to accommodate our world sim walker").
	/// </summary>
	/// <remarks>
	/// <c>-executeMethod FishMMO.TestHarness.World.Editor.CloudWorldWalkProbe.Run</c> in a GUI editor (play mode needs one;
	/// memory tools renderrun.sh): FISHMMO_SCENE the scene (The Kel Peaks by default), FISHMMO_WALK_OUT the report folder,
	/// FISHMMO_WALK_COUNT how many bridges (spread through the scene's; all with 0). Writes BridgeWalk-report.txt.
	/// </remarks>
	public static class CloudWorldWalkProbe
	{
		private const string StateKey = "FishMMO.CloudWorldWalkProbe.Out";
		private const string DefaultScene = "Assets/Scenes/WorldScene/Peliareth/The Kel Peaks.unity";
		/// <summary>How far onto the grass past each end the walk starts and finishes, metres.</summary>
		private const float Beyond = 2f;

		private static readonly List<string> report = new List<string>();
		private static readonly List<SceneCloudWorld.Crossing> walks = new List<SceneCloudWorld.Crossing>();
		private static readonly List<int> numbers = new List<int>();
		private static WorldSimWalker walker;
		private static int index;
		private static double started, lastProgressAt, settleUntil;
		private static float bestDistance;
		private static int passed, failed;
		private static bool placed;

		public static void Run()
		{
			string output = Environment.GetEnvironmentVariable("FISHMMO_WALK_OUT");
			if (string.IsNullOrEmpty(output))
			{
				output = Path.Combine(Path.GetTempPath(), "fishmmo-bridge-walk");
			}
			Directory.CreateDirectory(output);
			SessionState.SetString(StateKey, output);
			string scene = Environment.GetEnvironmentVariable("FISHMMO_SCENE");
			EditorSceneManager.OpenScene(string.IsNullOrEmpty(scene) ? DefaultScene : scene, OpenSceneMode.Single);
			EditorApplication.playModeStateChanged -= OnPlayMode;
			EditorApplication.playModeStateChanged += OnPlayMode;
			EditorApplication.EnterPlaymode();
		}

		[InitializeOnLoadMethod]
		private static void Resume()
		{
			if (string.IsNullOrEmpty(SessionState.GetString(StateKey, string.Empty)))
			{
				return;
			}
			EditorApplication.playModeStateChanged -= OnPlayMode;
			EditorApplication.playModeStateChanged += OnPlayMode;
			if (EditorApplication.isPlaying)
			{
				Begin();
			}
		}

		private static void OnPlayMode(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.EnteredPlayMode)
			{
				Begin();
			}
		}

		private static void Begin()
		{
			report.Clear();
			walks.Clear();
			numbers.Clear();
			index = -1;
			passed = failed = 0;
			settleUntil = EditorApplication.timeSinceStartup + 8.0;
			EditorApplication.update -= Pump;
			EditorApplication.update += Pump;
		}

		private static void Pump()
		{
			try
			{
				if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < settleUntil)
				{
					return;
				}
				if (index < 0)
				{
					SceneCloudWorld world = SceneCloudWorld.First;
					if (world == null || world.Bridges == null || world.Bridges.Length == 0)
					{
						report.Add("No cloud world with bridges in the scene (re-cut it).");
						Finish(1);
						return;
					}
					// FISHMMO_WALK_ONLY="36,45": those bridges (1-based, as the report numbers them) and no others.
					string only = Environment.GetEnvironmentVariable("FISHMMO_WALK_ONLY");
					if (!string.IsNullOrEmpty(only))
					{
						foreach (string part in only.Split(','))
						{
							if (int.TryParse(part.Trim(), out int number) && number >= 1 && number <= world.Bridges.Length)
							{
								walks.Add(world.Bridges[number - 1]);
								numbers.Add(number);
							}
						}
					}
					else
					{
						int count = int.TryParse(Environment.GetEnvironmentVariable("FISHMMO_WALK_COUNT"), out int n) ? n : 10;
						count = count <= 0 ? world.Bridges.Length : Mathf.Min(count, world.Bridges.Length);
						for (int i = 0; i < count; i++)
						{
							int at = (int)((long)i * world.Bridges.Length / count);
							walks.Add(world.Bridges[at]);
							numbers.Add(at + 1);
						}
					}
					walker = WorldSimWalker.Create(world);
					report.Add($"{world.Bridges.Length} bridge(s) in the scene; walking {walks.Count}.");
					index = 0;
					placed = false;
				}
				if (index >= walks.Count)
				{
					report.Add($"{passed} crossed, {failed} failed.");
					Finish(failed == 0 ? 0 : 1);
					return;
				}
				Step(walks[index]);
			}
			catch (Exception ex)
			{
				report.Add($"Threw: {ex}");
				Finish(1);
			}
		}

		private static void Step(SceneCloudWorld.Crossing bridge)
		{
			var along = new Vector3(bridge.To.x - bridge.From.x, 0f, bridge.To.z - bridge.From.z);
			float length = along.magnitude;
			along /= Mathf.Max(1e-3f, length);
			Vector3 start = bridge.From - along * Beyond, target = bridge.To + along * Beyond;
			double now = EditorApplication.timeSinceStartup;
			if (!placed)
			{
				walker.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
				walker.PlaceAt(start);
				placed = true;
				started = lastProgressAt = now;
				bestDistance = float.PositiveInfinity;
				return;
			}
			Vector3 feet = walker.Feet;
			var toTarget = new Vector3(target.x - feet.x, 0f, target.z - feet.z);
			float distance = toTarget.magnitude;
			string what = $"bridge {numbers[index]} ({bridge.From} → {bridge.To}, {length:0.0} m)";
			if (distance < 0.8f)
			{
				passed++;
				report.Add($"{what}: crossed in {now - started:0.0} s, landing at {feet}.");
				Next();
				return;
			}
			if (feet.y < Mathf.Min(bridge.From.y, bridge.To.y) - 4f)
			{
				failed++;
				report.Add($"{what}: FELL, {distance:0.0} m short, at {feet}.");
				Next();
				return;
			}
			if (distance < bestDistance - 0.25f)
			{
				bestDistance = distance;
				lastProgressAt = now;
			}
			else if (now - lastProgressAt > 6.0)
			{
				failed++;
				report.Add($"{what}: STUCK {distance:0.0} m short, at {feet} (grounded {walker.Grounded}); {Blocker(feet, toTarget / Mathf.Max(1e-3f, distance))}");
				Next();
				return;
			}
			Quaternion view = Quaternion.LookRotation(toTarget / Mathf.Max(1e-3f, distance), Vector3.up);
			walker.Move(1f, 0f, view, false, false, false, false, Mathf.Min(0.05f, Time.deltaTime));
		}

		/// <summary>What stands in the walker's way: a capsule of its size swept 0.6 m on toward the target, and what is under it.</summary>
		private static string Blocker(Vector3 feet, Vector3 toward)
		{
			PhysicsScene physics = walker.gameObject.scene.GetPhysicsScene();
			int mask = ~(1 << walker.gameObject.layer);
			Vector3 bottom = feet + Vector3.up * (walker.Radius + 0.05f), top = feet + Vector3.up * (walker.Height - walker.Radius);
			string ahead = physics.CapsuleCast(bottom, top, walker.Radius * 0.95f, toward, out RaycastHit hit, 0.6f, mask, QueryTriggerInteraction.Ignore)
				? $"ahead: '{NameOf(hit.collider.transform)}' (layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}) at {hit.point}, normal {hit.normal}, {hit.point.y - feet.y:0.00} m over the feet"
				: "nothing ahead within 0.6 m";
			string under = physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out RaycastHit down, 3f, mask, QueryTriggerInteraction.Ignore)
				? $"standing on '{NameOf(down.collider.transform)}' at {down.point.y:0.00}"
				: "nothing under";
			float terrain = float.NaN;
			foreach (Terrain t in Terrain.activeTerrains)
			{
				terrain = t.SampleHeight(feet) + t.transform.position.y;
			}
			// The chunk collider is every prop's merged: which ones stand by the contact.
			var near = new List<string>();
			Vector3 contact = hit.collider != null ? hit.point : feet;
			foreach (ScenePropSet set in Resources.FindObjectsOfTypeAll<ScenePropSet>())
			{
				if (set == null || set.Props == null)
				{
					continue;
				}
				foreach (ScenePropSet.Prop prop in set.Props)
				{
					float d = Vector3.Distance(prop.Position, contact);
					if (d < 3f && prop.Prototype >= 0 && prop.Prototype < set.Prototypes.Length && set.Prototypes[prop.Prototype].Prefab != null)
					{
						near.Add($"{set.Source}:{set.Prototypes[prop.Prototype].Prefab.name} {d:0.0} m at {prop.Position} rot {prop.Rotation.eulerAngles} scale {prop.Scale}");
					}
				}
			}
			return $"{ahead}; {under}; terrain {terrain:0.00}; props by it: {(near.Count > 0 ? string.Join(" | ", near) : "none")}.";
		}

		private static string NameOf(Transform t)
		{
			string path = t.name;
			for (Transform p = t.parent; p != null && path.Length < 120; p = p.parent)
			{
				path = p.name + "/" + path;
			}
			return path;
		}

		private static void Next()
		{
			Debug.Log($"[Bridge walk] {report[report.Count - 1]}");
			index++;
			placed = false;
		}

		private static void Finish(int code)
		{
			EditorApplication.update -= Pump;
			string output = SessionState.GetString(StateKey, ".");
			SessionState.EraseString(StateKey);
			foreach (string line in report)
			{
				Debug.Log($"[Bridge walk] {line}");
			}
			report.Add(code == 0 ? "DONE" : "FAILED");
			File.WriteAllLines(Path.Combine(output, "BridgeWalk-report.txt"), report);
			EditorAutomation.Finish(code);
		}
	}
}
