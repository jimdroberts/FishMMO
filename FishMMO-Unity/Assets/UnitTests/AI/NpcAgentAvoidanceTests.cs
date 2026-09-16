using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins crowd avoidance OFF on every NPC's agent (issue #220): the NavMesh crowd is global
	/// across stacked scene instances, so avoidance made NPCs dodge NPCs in other instances.
	/// AISeparation and AICombatSlots space them instead.
	/// </summary>
	/// <remarks>
	/// The agent is no longer authored on the prefab: the server adds it with the brain at spawn,
	/// so the setting is pinned where it is made — <c>AIController.InitializeOnce</c> — and the
	/// prefabs are pinned to carry no agent that could disagree with it.
	/// </remarks>
	[TestFixture]
	public class NpcAgentAvoidanceTests
	{
		[Test]
		public void TheBrainTurnsCrowdAvoidanceOff()
		{
			string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
				"Assets/Scripts/Server/Implementation/World/SceneServer/AI/AIController.cs"));

			Assert.That(source, Does.Contain("Agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;"),
				"the brain must switch crowd avoidance off on the agent it runs");
		}

		[Test]
		public void NoPrefab_AuthorsANavMeshAgent()
		{
			List<string> offenders = new List<string>();
			int scanned = 0;

			foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
				{
					continue;
				}
				scanned++;
				if (File.ReadAllText(path).Contains("\nNavMeshAgent:"))
				{
					offenders.Add(path);
				}
			}

			Assert.That(scanned, Is.GreaterThan(0), "no prefabs found; the pin is vacuous");
			Assert.That(offenders, Is.Empty,
				"a NavMeshAgent is server-only and added with the brain at spawn; authored on a prefab it ships to clients");
		}
	}
}
