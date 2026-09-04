using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins crowd avoidance OFF on every NPC prefab (issue #220): the NavMesh crowd is global
	/// across stacked scene instances, so avoidance made NPCs dodge NPCs in other instances.
	/// AISeparation and AICombatSlots space them instead. Read from YAML, and enforced in code by
	/// AIController.InitializeOnce as well, so this pins the authored intent.
	/// </summary>
	[TestFixture]
	public class NpcAgentAvoidanceTests
	{
		[Test]
		public void EveryNavMeshAgentPrefab_HasObstacleAvoidanceOff()
		{
			List<string> offenders = new List<string>();
			int checkedCount = 0;

			foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
				{
					continue;
				}
				string text = File.ReadAllText(path);
				if (!text.Contains("\nNavMeshAgent:"))
				{
					continue;
				}
				checkedCount++;
				foreach (Match m in Regex.Matches(text, @"^\s+m_ObstacleAvoidanceType:\s*(\d+)\s*$", RegexOptions.Multiline))
				{
					if (m.Groups[1].Value != "0")
					{
						offenders.Add(path);
					}
				}
			}

			Assert.That(checkedCount, Is.GreaterThan(0), "no NavMeshAgent prefabs found; the pin is vacuous");
			Assert.That(offenders, Is.Empty, "NavMeshAgent.obstacleAvoidanceType must be NoObstacleAvoidance on NPCs");
		}
	}
}
