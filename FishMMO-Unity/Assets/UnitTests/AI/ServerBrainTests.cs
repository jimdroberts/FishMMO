using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Object;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the move of the NPC brain to the server: where the code lives, how shared code reaches
	/// it, and how the server attaches it to an NPC that carries none.
	/// </summary>
	[TestFixture]
	public class ServerBrainTests
	{
		private const string ORC = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string BANKER = "Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab";

		private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (UnityEngine.Object o in created)
			{
				if (o != null)
				{
					UnityEngine.Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		/// <summary>
		/// An edit-mode instance of an NPC prefab, with the references <c>BaseCharacter.Awake</c>
		/// would have set — Awake does not run outside play mode.
		/// </summary>
		private NPC InstantiateNPC(string path)
		{
			GameObject instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
			created.Add(instance);
			NPC npc = instance.GetComponent<NPC>();
			System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.GameObject), flags).SetValue(npc, instance);
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.Transform), flags).SetValue(npc, instance.transform);
			return npc;
		}

		private static AIBrainCatalogue LoadCatalogue()
		{
			AIBrainCatalogue catalogue = AssetDatabase.LoadAssetAtPath<AIBrainCatalogue>(BrainCatalogueYaml.CataloguePath);
			Assert.IsNotNull(catalogue, $"no brain catalogue at {BrainCatalogueYaml.CataloguePath}");
			catalogue.Invalidate();
			return catalogue;
		}

		// --- Where the code lives ----------------------------------------------------------------

		[Test]
		public void NoBrainOrSpawnerType_IsCompiledIntoSharedCode()
		{
			/* The point of the move: a client build compiles FishMMO.Shared and never FishMMO.Server,
			 * so anything left in Shared ships to every player. */
			HashSet<string> serverTypes = new HashSet<string>(typeof(AIController).Assembly.GetTypes()
				.Where(t => t.Namespace == typeof(AIController).Namespace || t.Namespace == typeof(ObjectSpawner).Namespace)
				.Select(t => t.Name));
			Assert.That(serverTypes, Does.Contain(nameof(AIController)).And.Contain(nameof(ObjectSpawner)));

			string[] leaked = typeof(NPC).Assembly.GetTypes()
				.Where(t => serverTypes.Contains(t.Name) && !t.IsNested)
				.Select(t => t.FullName)
				.ToArray();
			Assert.IsEmpty(leaked, "server-only AI or spawner types found in FishMMO.Shared");

			Assert.AreEqual("FishMMO.Server", typeof(AIController).Assembly.GetName().Name);
			Assert.AreEqual("FishMMO.Server", typeof(ObjectSpawner).Assembly.GetName().Name);
		}

		[Test]
		public void TheServerAssembly_IsNotCompiledForClientPlayers()
		{
			string json = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Server/FishMMO.Server.asmdef"));
			ServerAsmdef asmdef = JsonUtility.FromJson<ServerAsmdef>(json);

			Assert.That(asmdef.includePlatforms, Does.Contain("Editor"));
			Assert.That(asmdef.includePlatforms, Has.None.Matches<string>(p => p == "LinuxStandalone64" || p == "WindowsStandalone64" || p == "WebGL"),
				"the server assembly must never be built into a client player");
		}

		[Serializable]
		private class ServerAsmdef
		{
			public string[] includePlatforms = Array.Empty<string>();
		}

		[Test]
		public void TheBrain_IsAPlainComponentThatSharedCodeReachesThroughINPCBrain()
		{
			/* FishNet indexes a NetworkObject's behaviours by the components present at spawn; a
			 * server-only NetworkBehaviour would shift that index on the server alone. */
			Assert.IsFalse(typeof(NetworkBehaviour).IsAssignableFrom(typeof(AIController)),
				"the brain is added at runtime on the server only, so it must not be a NetworkBehaviour");
			Assert.IsTrue(typeof(INPCBrain).IsAssignableFrom(typeof(AIController)));
			Assert.IsTrue(typeof(ICharacterBehaviour).IsAssignableFrom(typeof(AIController)),
				"the brain must register with its character so TryGet finds it");
			Assert.IsFalse(typeof(NetworkBehaviour).IsAssignableFrom(typeof(ObjectSpawner)),
				"the spawner is authoring-only and must not be networked");
		}

		[Test]
		public void ServerDrivenInput_IsDecidedByTheNPCType_NotByABrainComponent()
		{
			/* A pet is owned by its summoner's connection. If "server drives this input" were read
			 * from the brain component — which clients never have — the owner's client would start
			 * writing the pet's input. */
			string root = Directory.GetCurrentDirectory();
			string prediction = File.ReadAllText(Path.Combine(root,
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterPredictionController.cs"));
			string lagComp = File.ReadAllText(Path.Combine(root,
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/LagCompensation/LagCompensationTick.cs"));

			Assert.That(prediction, Does.Contain("serverDrivenInput = GetComponent<NPC>() != null;"));
			Assert.That(lagComp, Does.Contain("if (nob.TryGetComponent(out NPC _))"));
			Assert.IsTrue(typeof(NPC).IsAssignableFrom(typeof(Pet)), "pets must count as server-driven");
		}

		// --- The catalogue -----------------------------------------------------------------------

		[Test]
		public void TheCatalogue_FindsAnNPCsBrainByItsPrefabIdentity()
		{
			AIBrainCatalogue catalogue = LoadCatalogue();
			NetworkObject orc = AssetDatabase.LoadAssetAtPath<GameObject>(ORC).GetComponent<NetworkObject>();
			NetworkObject banker = AssetDatabase.LoadAssetAtPath<GameObject>(BANKER).GetComponent<NetworkObject>();

			Assert.IsTrue(catalogue.TryGetEntry(orc, out AIBrainCatalogue.Entry orcEntry));
			Assert.IsTrue(catalogue.TryGetEntry(banker, out AIBrainCatalogue.Entry bankerEntry));
			Assert.AreEqual("Enemy - Melee", orcEntry.Archetype.name);
			Assert.AreEqual("Civilian - Townsfolk", bankerEntry.Archetype.name);

			/* A spawned instance carries its prefab's identity, so an instance finds the same entry. */
			GameObject instance = UnityEngine.Object.Instantiate(orc.gameObject);
			created.Add(instance);
			Assert.IsTrue(catalogue.TryGetEntry(instance.GetComponent<NetworkObject>(), out AIBrainCatalogue.Entry instanceEntry));
			Assert.AreSame(orcEntry, instanceEntry);
		}

		[Test]
		public void TheCatalogue_KeepsBrainDataOutOfClientBundles()
		{
			/* The catalogue is reached only from the AISystem asset, which is in the server-only
			 * addressable group; a client-side group listing it would ship every brain. */
			string root = Directory.GetCurrentDirectory();
			string catalogueGuid = BrainCatalogueYaml.GuidOf(BrainCatalogueYaml.FullPath);
			string aiSystemGuid = BrainCatalogueYaml.GuidOf(Path.Combine(root, "Assets/Prefabs/Server/SceneServer/AISystem.asset"));
			string spawnerSystemGuid = BrainCatalogueYaml.GuidOf(Path.Combine(root, "Assets/Prefabs/Server/SceneServer/SpawnerSystem.asset"));
			Assert.IsNotNull(catalogueGuid);
			Assert.IsNotNull(aiSystemGuid);
			Assert.IsNotNull(spawnerSystemGuid);

			string groups = Path.Combine(root, "Assets/AddressableAssetsData/AssetGroups");
			foreach (string group in Directory.GetFiles(groups, "*.asset"))
			{
				string text = File.ReadAllText(group);
				bool serverGroup = Path.GetFileNameWithoutExtension(group).StartsWith("Server", StringComparison.Ordinal);
				if (serverGroup)
				{
					continue;
				}
				Assert.That(text, Does.Not.Contain(catalogueGuid), $"{group} lists the brain catalogue");
				Assert.That(text, Does.Not.Contain(aiSystemGuid), $"{group} lists the AI system");
				Assert.That(text, Does.Not.Contain(spawnerSystemGuid), $"{group} lists the spawner system");
			}

			string serverGroupText = File.ReadAllText(Path.Combine(groups, "Server_Static_Permanent.asset"));
			Assert.That(serverGroupText, Does.Contain(aiSystemGuid), "the AI system must be in the server-only group");
			Assert.That(serverGroupText, Does.Contain(spawnerSystemGuid), "the spawner system must be in the server-only group");

			string sceneServer = File.ReadAllText(Path.Combine(root, "Assets/Scenes/Server/SceneServer.unity"));
			Assert.That(sceneServer, Does.Contain(aiSystemGuid), "the scene server must run the AI system");
			Assert.That(sceneServer, Does.Contain(spawnerSystemGuid), "the scene server must run the spawner system");
		}

		// --- The host ----------------------------------------------------------------------------

		[Test]
		public void Prepare_AttachesTheCataloguedBrainAndResetForgetsIt()
		{
			// An edit-mode NPC has no NavMesh to stand on; the agent's complaints about that are expected.
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			GameObject orcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ORC);
			Assert.IsNull(orcPrefab.GetComponent<AIController>(), "the prefab must carry no brain of its own");

			NPC npc = InstantiateNPC(ORC);
			GameObject instance = npc.gameObject;

			AIBrainHost host = new AIBrainHost(null, LoadCatalogue());
			AIController brain = host.Prepare(npc, new Vector3(1f, 0f, 2f));

			Assert.IsNotNull(brain);
			Assert.AreSame(brain, instance.GetComponent<AIController>());
			Assert.IsNotNull(instance.GetComponent<NavMeshAgent>(), "the agent arrives with the brain");
			Assert.IsTrue(brain.Prepared);
			Assert.AreEqual("Enemy - Melee", brain.Archetype.name, "the prefab's catalogue entry names the brain");
			Assert.AreEqual(1, host.Count);
			Assert.IsTrue(npc.TryGet(out INPCBrain registered) && ReferenceEquals(registered, brain),
				"shared code must reach the brain through the character's behaviour registry");

			// A spawner's override beats the catalogue, and the next spawn goes back to the catalogue.
			AIArchetypeTemplate other = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(
				"Assets/Templates/Entity/NPCs/AI/Archetypes/Enemy - Archer.asset");
			Assert.IsNotNull(other);
			host.Prepare(npc, Vector3.zero, other);
			Assert.AreSame(other, brain.Archetype);
			Assert.AreEqual(1, host.Count, "preparing again must not tick the brain twice");

			brain.ResetForPool();
			Assert.IsFalse(brain.Prepared);
			Assert.IsNull(brain.CurrentState);

			host.Prepare(npc, Vector3.zero);
			Assert.AreEqual("Enemy - Melee", brain.Archetype.name,
				"a pooled instance must not carry the previous spawner's brain into its next life");
			Assert.AreSame(brain, instance.GetComponent<AIController>(), "a pooled NPC keeps its one brain");
		}

		[Test]
		public void SuspendForCorpse_StopsTheBrainAndResumeStartsIt()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

			NPC npc = InstantiateNPC(ORC);
			AIBrainHost host = new AIBrainHost(null, LoadCatalogue());
			AIController brain = host.Prepare(npc, Vector3.zero);

			Assert.IsTrue(brain.SuspendForCorpse(), "a running brain reports that the corpse stopped it");
			Assert.IsFalse(brain.enabled);
			Assert.IsNull(brain.Target);
			Assert.IsFalse(brain.SuspendForCorpse(), "a second suspension did not stop anything");

			brain.ResumeAfterCorpse();
			Assert.IsTrue(brain.enabled);
		}

		// --- Spawner host ------------------------------------------------------------------------

		[Test]
		public void SpawnerHost_RunsATablePerSceneInstanceAndDropsItOnUnload()
		{
			SpawnerHost host = new SpawnerHost(null, null);
			SceneSpawnTable table = ScriptableObject.CreateInstance<SceneSpawnTable>();
			created.Add(table);
			table.SceneName = "Test";
			table.Spawners.Add(new SpawnerDefinition { Name = "A", Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings() } });
			table.Spawners.Add(new SpawnerDefinition { Name = "B", Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings() } });

			UnityEngine.SceneManagement.Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
			try
			{
				IReadOnlyList<SpawnerRuntime> spawners = host.StartScene(scene, table);

				Assert.AreEqual(2, spawners.Count);
				Assert.AreEqual(1, host.SceneCount);
				Assert.IsTrue(spawners.All(s => s.Running));
				Assert.AreSame(spawners[1], spawners[0].GetSibling(1), "a condition must be able to name a sibling by index");
				Assert.IsNull(spawners[0].GetSibling(5));
				Assert.AreEqual(1, spawners[0].PendingRespawnCount, "with no network the one slot is owed as a respawn");

				host.StopScene(scene.handle);

				Assert.AreEqual(0, host.SceneCount);
				Assert.IsTrue(spawners.All(s => !s.Running));
				Assert.AreEqual(0, host.Scheduler.ActiveCount, "an unloaded scene's spawners must leave the schedule");
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void SpawnersClearedCondition_WaitsForTheNamedSpawnerOnly()
		{
			SpawnerScheduler scheduler = new SpawnerScheduler();
			List<SpawnerRuntime> siblings = new List<SpawnerRuntime>();
			SpawnerDefinition guardDefinition = new SpawnerDefinition { Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings() } };
			SpawnerDefinition campDefinition = new SpawnerDefinition { Spawnables = new List<SpawnableSettings> { new ItemSpawnableSettings() } };
			SpawnerRuntime guard = new SpawnerRuntime(guardDefinition, default, null, scheduler, siblings);
			SpawnerRuntime camp = new SpawnerRuntime(campDefinition, default, null, scheduler, siblings);
			siblings.Add(guard);
			siblings.Add(camp);

			SpawnersClearedCondition condition = new SpawnersClearedCondition();
			condition.SpawnerIndices.Add(0);

			Assert.IsTrue(condition.OnCheckCondition(camp), "nothing guards an empty camp");

			// A non-character spawn has no health, so it never holds the camp back.
			guard.Track(new FakeSpawnable(1), null);
			Assert.IsTrue(condition.OnCheckCondition(camp));

			condition.SpawnerIndices.Add(9);
			Assert.IsTrue(condition.OnCheckCondition(camp), "an index outside the table is ignored, not thrown on");
		}
	}
}
