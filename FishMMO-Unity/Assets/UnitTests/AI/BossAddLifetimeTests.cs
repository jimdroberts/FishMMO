using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// A boss's adds: which NPCs are the boss's to send away, that a leash reset sends them away,
	/// and the cap on how many a timed mechanic keeps alive.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What was wrong.</b> <c>BossScriptState.SpawnAdds</c> put its adds in the boss's pack and
	/// kept no record of them, so a leash reset (<c>BossScript.ResetOnLeash</c>) rewound the phases
	/// and left every add standing beside a full-health boss about to call the same adds in again.
	/// The pack could not stand in for the record — it is often the boss's spawner's, and its other
	/// members were never the boss's — and a timed mechanic, firing for as long as the fight lasts,
	/// had no ceiling at all.
	/// </para>
	/// <para>
	/// The membership rules run on live edit-mode brains, as the pack tests do. Despawning needs a
	/// network manager, which edit mode has none of, so the dismissal is pinned on its bookkeeping
	/// behaviourally and on its despawn route by source scan; every scan carries its own control run
	/// (<see cref="SourceScanPins.HoldsAndFires"/>).
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BossAddLifetimeTests
	{
		private const string ORC = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string AI_ROOT = "Assets/Scripts/Server/Implementation/World/SceneServer/AI/";

		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			AggressionDispatcher.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		// --- The cap, as a truth table ----------------------------------------------------------------

		[Test]
		public void ATimedMechanic_CallsInOnlyBelowTheCap_AndAPhaseAlways()
		{
			LogAssert.IsTrue(BossScriptState.MayCallInAdd(false, 0, 12), "a mechanic calls in its first add");
			LogAssert.IsTrue(BossScriptState.MayCallInAdd(false, 11, 12), "and one more below the cap");
			LogAssert.IsFalse(BossScriptState.MayCallInAdd(false, 12, 12), "at the cap a mechanic calls in nothing");
			LogAssert.IsFalse(BossScriptState.MayCallInAdd(false, 40, 12), "nor past it");

			LogAssert.IsTrue(BossScriptState.MayCallInAdd(true, 12, 12), "a phase's adds arrive at the cap: a phase is entered once per pull");
			LogAssert.IsTrue(BossScriptState.MayCallInAdd(true, 40, 12), "and past it");

			LogAssert.IsTrue(BossScriptState.MayCallInAdd(false, 0, 0), "a cap below one is one, never 'none'");
			LogAssert.IsFalse(BossScriptState.MayCallInAdd(false, 1, 0), "so a second add waits");
			LogAssert.IsFalse(BossScriptState.MayCallInAdd(false, 1, -5), "whatever the negative number");
		}

		[Test]
		public void TheCap_DefaultsToOneCombatRing_AndCannotBeAuthoredBelowOne()
		{
			BossScript script = ScriptableObject.CreateInstance<BossScript>();
			created.Add(script);

			/* The default's justification is AICombatSlots' ring: past it an add only waits on an
			 * outer ring. If the ring changes, this fails, so the cap is re-decided rather than
			 * quietly drifting away from its reason. */
			FieldInfo ring = typeof(AICombatSlots).GetField("MAX_RING_CAPACITY", BindingFlags.NonPublic | BindingFlags.Static);
			LogAssert.IsNotNull(ring, "AICombatSlots.MAX_RING_CAPACITY is gone; re-justify BossScript.MaxLiveAdds");
			LogAssert.AreEqual((int)ring.GetValue(null), script.MaxLiveAdds, "the default cap is one full ring around a target");

			MinAttribute min = typeof(BossScript).GetField(nameof(BossScript.MaxLiveAdds)).GetCustomAttribute<MinAttribute>();
			LogAssert.IsTrue(min != null && min.min == 1f, "a cap of zero would read as 'no adds' to one author and 'no cap' to another");
		}

		// --- Whose adds they are ----------------------------------------------------------------------

		[Test]
		public void AnAdd_LeavesItsBoss_OnDeath_OnDespawn_AndWhenAnotherBossTakesIt()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host);
			AIController b = PrepareOrc(host);
			BossScriptState boss = NewState();

			boss.AdoptAdd(a);
			boss.AdoptAdd(b);
			boss.AdoptAdd(b);
			LogAssert.AreEqual(2, boss.LiveAddCount, "adopting the same add twice counts it once");
			LogAssert.IsTrue(ReferenceEquals(boss, a.Summoner) && ReferenceEquals(boss, b.Summoner), "each add holds its boss");

			a.SuspendForCorpse();
			LogAssert.IsNull(a.Summoner, "a dead add is no longer the boss's to dismiss: its corpse keeps its loot");
			LogAssert.AreEqual(1, boss.LiveAddCount, "and no longer counts towards the cap");

			host.RetireBrain(b);
			LogAssert.IsNull(b.Summoner, "a despawn resets the brain for the pool, and the reset leaves the boss");
			LogAssert.AreEqual(0, boss.LiveAddCount, "so the pool's next NPC is never dismissed by a boss it never met");

			AIController c = PrepareOrc(host);
			BossScriptState other = NewState();
			boss.AdoptAdd(c);
			other.AdoptAdd(c);
			LogAssert.AreEqual(0, boss.LiveAddCount, "an add is one boss's at a time");
			LogAssert.AreEqual(1, other.LiveAddCount);
			LogAssert.IsTrue(ReferenceEquals(other, c.Summoner), "and it is the boss that took it last");
		}

		[Test]
		public void ABossThatDies_IsPooled_OrChangesScript_LetsGoOfItsAdds_WithoutStoppingThem()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();

			AIController dying = NewBoss(host);
			AIController add = PrepareOrc(host);
			dying.BossState.AdoptAdd(add);
			dying.SuspendForCorpse();
			LogAssert.IsNull(add.Summoner, "a dead boss lets go of its adds");
			LogAssert.AreEqual(0, dying.BossState.LiveAddCount);
			LogAssert.IsTrue(add.IsRunning, "and they fight on, as they always did: only a leash sends them away");

			AIController pooled = NewBoss(host);
			AIController add2 = PrepareOrc(host);
			pooled.BossState.AdoptAdd(add2);
			host.RetireBrain(pooled);
			LogAssert.IsNull(add2.Summoner, "a pooled boss's next occupant inherits no adds");
			LogAssert.IsTrue(add2.IsRunning, "and the adds are not stopped by it");

			AIController rescripted = NewBoss(host);
			AIController add3 = PrepareOrc(host);
			BossScriptState old = rescripted.BossState;
			old.AdoptAdd(add3);
			rescripted.BossScript = NewScript();
			LogAssert.IsNull(add3.Summoner, "a replaced boss state holds nobody");
			LogAssert.AreEqual(0, old.LiveAddCount);
		}

		[Test]
		public void ALeashDismissal_EmptiesTheList_UnlinksEveryAdd_AndSkipsTheDestroyed()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host);
			AIController b = PrepareOrc(host);
			AIController gone = PrepareOrc(host);
			BossScriptState boss = NewState();
			boss.AdoptAdd(a);
			boss.AdoptAdd(b);
			boss.AdoptAdd(gone);

			// Destroyed with its scene: in edit mode OnDestroy does not run, so it is still listed.
			Object.DestroyImmediate(gone.gameObject);

			// Nothing is network-spawned in edit mode, so nothing despawns; the bookkeeping is the point.
			int dismissed = boss.DismissAdds(null);
			LogAssert.AreEqual(0, dismissed, "an unspawned add is not counted as despawned");
			LogAssert.AreEqual(0, boss.LiveAddCount, "every add leaves the list, the destroyed one included");
			LogAssert.IsNull(a.Summoner, "each add is unlinked, so its own pool reset does not come back to a boss mid-walk");
			LogAssert.IsNull(b.Summoner);
		}

		// --- The routes, by source scan ---------------------------------------------------------------

		[Test]
		public void EveryLeashThatResetsTheBoss_DismissesItsAdds_ThroughThePool()
		{
			string brain = SourceScanPins.ReadCode(AI_ROOT + "AIController.cs");

			SourceScanPins.HoldsAndFires("ResetBossScriptForLeash", brain,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private void ResetBossScriptForLeash()"),
					"BossState?.DismissAdds(NetworkManager);", "ResetBossScript();"),
				SourceScanPins.Replace("BossState?.DismissAdds(NetworkManager);", string.Empty),
				"a leash reset rewinds the phases and leaves the adds standing");

			/* The warp, the walk home that ends a fight, and the LOD soft leash: each honours
			 * ResetOnLeash through the leash reset, never the bare phase reset a despawn uses. */
			SourceScanPins.HoldsAndFires("leash call sites", brain,
				code =>
				{
					int calls = System.Text.RegularExpressions.Regex.Matches(code, @"ResetBossScriptForLeash\(\);").Count;
					if (calls != 3)
					{
						return $"expected the three leash paths to call ResetBossScriptForLeash, found {calls}";
					}
					return System.Text.RegularExpressions.Regex.IsMatch(code, @"ResetOnLeash\)\s*\{\s*ResetBossScript\(\);")
						? "a leash path resets the phases without dismissing the adds"
						: null;
				},
				SourceScanPins.RegexReplaceFirst(@"ResetBossScriptForLeash\(\);", "ResetBossScript();"),
				"one leash path went back to the bare phase reset");

			string state = SourceScanPins.ReadCode(AI_ROOT + "Boss/BossScriptState.cs");
			SourceScanPins.HoldsAndFires("DismissAdds", state,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "internal int DismissAdds("),
					"liveAdds.RemoveAt(i);", "add.Summoner = null;", "PersistentPool.Despawn(networkManager, add.NetworkObject)"),
				SourceScanPins.Replace("PersistentPool.Despawn(networkManager, add.NetworkObject)", "networkManager.ServerManager.Despawn(add.NetworkObject) != null"),
				"an add despawned outside PersistentPool, left in the world scene its unload destroys");
		}

		[Test]
		public void DeathDespawnAndDestruction_LeaveTheBoss_AndReleaseRatherThanDismiss()
		{
			string brain = SourceScanPins.ReadCode(AI_ROOT + "AIController.cs");
			foreach (string signature in new[] { "public bool SuspendForCorpse()", "internal void ResetForPool()", "private void OnDestroying()" })
			{
				SourceScanPins.HoldsAndFires(signature, brain,
					code =>
					{
						string body = SourceScanPins.Body(code, signature);
						if (body == null)
						{
							return "the body was not found";
						}
						if (!body.Contains("LeaveSummoner();"))
						{
							return "does not leave its boss";
						}
						if (!body.Contains("BossState?.ReleaseAdds();"))
						{
							return "does not let go of its own adds";
						}
						return body.Contains("DismissAdds") ? "dismisses adds outside a leash" : null;
					},
					code => code.Replace(SourceScanPins.Body(code, signature), SourceScanPins.Body(code, signature).Replace("LeaveSummoner();", string.Empty)),
					$"{signature} keeps its boss");
			}
		}

		[Test]
		public void SpawnAdds_CapsTimedMechanics_AndRecordsEveryAdd()
		{
			string state = SourceScanPins.ReadCode(AI_ROOT + "Boss/BossScriptState.cs");

			SourceScanPins.HoldsAndFires("SpawnAdds", state,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private void SpawnAdds("),
					"MayCallInAdd(phaseAdds, liveAdds.Count, maxLiveAdds)", "SpawnAdd(networkManager", "AdoptAdd(add);", "pack.AddMember(add, NPCGroupRole.DPS);"),
				SourceScanPins.Replace("AdoptAdd(add);", string.Empty),
				"an add spawned and never recorded as the boss's, so no leash can dismiss it");

			SourceScanPins.HoldsAndFires("ExecuteMechanic", state,
				code => SourceScanPins.Body(code, "private void ExecuteMechanic(")?.Contains("mechanic.SpawnOffsets, phaseAdds: false);") == true
					? null
					: "a timed mechanic's adds are not held to the cap",
				SourceScanPins.Replace("mechanic.SpawnOffsets, phaseAdds: false);", "mechanic.SpawnOffsets, phaseAdds: true);"),
				"a timed mechanic spawning as though it were a phase, uncapped");
		}

		// --- Helpers ----------------------------------------------------------------------------------

		private BossScript NewScript()
		{
			BossScript script = ScriptableObject.CreateInstance<BossScript>();
			created.Add(script);
			return script;
		}

		private BossScriptState NewState() => new BossScriptState(NewScript());

		private AIController NewBoss(AIBrainHost host)
		{
			AIController boss = PrepareOrc(host);
			boss.BossScript = NewScript();
			LogAssert.IsNotNull(boss.BossState, "assigning a boss script starts its state");
			return boss;
		}

		private static AIBrainHost NewHost()
		{
			AIBrainCatalogue catalogue = AssetDatabase.LoadAssetAtPath<AIBrainCatalogue>(BrainCatalogueYaml.CataloguePath);
			LogAssert.IsNotNull(catalogue, $"no brain catalogue at {BrainCatalogueYaml.CataloguePath}");
			catalogue.Invalidate();
			return new AIBrainHost(null, catalogue);
		}

		/// <summary>
		/// An edit-mode orc, prepared by <paramref name="host"/>, with the references
		/// <c>BaseCharacter.Awake</c> would have set. The same rig the pack tests use.
		/// </summary>
		private AIController PrepareOrc(AIBrainHost host)
		{
			GameObject instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ORC));
			created.Add(instance);
			NPC npc = instance.GetComponent<NPC>();
			BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.GameObject), flags).SetValue(npc, instance);
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.Transform), flags).SetValue(npc, instance.transform);

			AIController brain = host.Prepare(npc, Vector3.zero);
			LogAssert.IsNotNull(brain, "the host prepared no brain for the orc");
			LogAssert.IsTrue(brain.IsRunning, "the prepared orc's brain is not running");
			return brain;
		}
	}
}
