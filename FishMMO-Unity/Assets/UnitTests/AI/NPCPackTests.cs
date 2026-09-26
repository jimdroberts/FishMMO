using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins spawner-owned NPC packs (optional change O33): who is in a pack and when they leave, when
	/// a pack is released, how a respawn finds its pack again, how the brain host ticks packs inside
	/// the AI tick and LOD contract, the tactic geometry, and the boss adds that join a boss's pack.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What was dormant.</b> <c>NPCGroup</c> was a scene MonoBehaviour with inspector-assigned
	/// members and its own <c>Update</c>. Brains are added to pooled NPCs at runtime on the server, so
	/// nothing could ever be assigned to it, and <c>AIController.Group</c> was never set — group
	/// alerts, role targeting and the adopt-focus node never ran.
	/// </para>
	/// <para>
	/// Edit-mode orcs have no resource instance, so every one of them reads as dead; the liveness
	/// rules are therefore pinned as truth tables and the membership rules on live brains.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NPCPackTests
	{
		private const string ORC = "Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab";
		private const string AI_ROOT = "Assets/Scripts/Server/Implementation/World/SceneServer/AI/";

		private const float NETWORK_TICK = 1f / 30f;
		private const int TICKS_PER_AI_UPDATE = 4;
		private const float AI_TICK = NETWORK_TICK * TICKS_PER_AI_UPDATE;

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
			AggressionDispatcher.Clear();
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
		}

		// --- The rules, as truth tables ------------------------------------------------------------

		[Test]
		public void APacksTier_IsItsMostActiveMember_AndOnlyTheFightingTiersEvaluate()
		{
			Assert.AreEqual(AILodTier.Active, NPCGroup.MostActiveTier(AILodTier.Active, AILodTier.Dormant));
			Assert.AreEqual(AILodTier.Nearby, NPCGroup.MostActiveTier(AILodTier.Far, AILodTier.Nearby));
			Assert.AreEqual(AILodTier.Dormant, NPCGroup.MostActiveTier(AILodTier.Dormant, AILodTier.Dormant));

			Assert.IsTrue(NPCGroup.TierEvaluates(AILodTier.Active));
			Assert.IsTrue(NPCGroup.TierEvaluates(AILodTier.Nearby), "a Nearby brain still fights, so its pack still coordinates");
			Assert.IsFalse(NPCGroup.TierEvaluates(AILodTier.Far), "a Far brain drops its fight; nothing is left to coordinate");
			Assert.IsFalse(NPCGroup.TierEvaluates(AILodTier.Dormant), "a Dormant brain runs nothing, and neither does its pack");

			Assert.AreEqual(NPCGroup.EVALUATE_INTERVAL, NPCGroup.ResolveEvaluateInterval(1), 1e-6f);
			Assert.AreEqual(NPCGroup.EVALUATE_INTERVAL * 3f, NPCGroup.ResolveEvaluateInterval(3), 1e-6f,
				"a pack slows with its members' LOD interval");
			Assert.AreEqual(NPCGroup.EVALUATE_INTERVAL, NPCGroup.ResolveEvaluateInterval(0), 1e-6f, "never faster than Active");
		}

		[Test]
		public void AMemberAnswersAnAlert_OnlyWhenItIsFreeToFight()
		{
			// hasAttackingState, running, inCombatState, evading, immortal, alive, enemyValid
			Assert.IsTrue(AIController.MayAnswerPackAlert(true, true, false, false, false, true, true),
				"a calm, living, mortal member joins its packmate's fight — a calm stroll home included");
			Assert.IsFalse(AIController.MayAnswerPackAlert(true, true, true, false, false, true, true),
				"a member already fighting (orbiting, fleeing) is not yanked onto the alert's enemy");
			Assert.IsFalse(AIController.MayAnswerPackAlert(true, true, false, true, false, true, true),
				"an evading member is not dragged back into the fight it leashed out of");
			Assert.IsFalse(AIController.MayAnswerPackAlert(true, true, false, false, true, true, true),
				"an immortal member has no reason to target anything");
			Assert.IsFalse(AIController.MayAnswerPackAlert(true, true, false, false, false, false, true), "a corpse does not answer");
			Assert.IsFalse(AIController.MayAnswerPackAlert(true, false, false, false, false, true, true),
				"an unprepared or quarantined brain does nothing");
			Assert.IsFalse(AIController.MayAnswerPackAlert(false, true, false, false, false, true, true), "a civilian never fights");
			Assert.IsFalse(AIController.MayAnswerPackAlert(true, true, false, false, false, true, false), "nor against a dead enemy");
		}

		[Test]
		public void OnlyAWoundedMember_NeedsHelp()
		{
			Assert.IsFalse(NPCGroup.IsWounded(1f), "a pack at full health has no most-wounded member for a defender to shield");
			Assert.IsTrue(NPCGroup.IsWounded(0.99f));
			Assert.IsTrue(NPCGroup.IsWounded(0f));
		}

		// --- Tactic geometry -------------------------------------------------------------------------

		[Test]
		public void Surround_LeavesAnEvenRingWhereItIs()
		{
			float[] angles = Formation(PackTactic.Surround, Roles(4), Deg(10f, 100f, 190f, 280f));
			AssertAngles(Deg(10f, 100f, 190f, 280f), angles, "members already on an even ring stay put");
		}

		[Test]
		public void Surround_SpreadsABunchedPackEvenly_InTheOrderTheyStand()
		{
			float[] bearings = Deg(0f, 10f, 20f, 30f);
			float[] angles = Formation(PackTactic.Surround, Roles(4), bearings);

			for (int i = 0; i < 4; ++i)
			{
				float gap = NPCGroup.Wrap(angles[(i + 1) % 4] - angles[i]);
				Assert.AreEqual(Mathf.PI * 0.5f, gap, 1e-3f, $"member {i} and the next are not a quarter-ring apart");
			}
		}

		[Test]
		public void Kite_TurnsTheFittedRing()
		{
			float advance = 0.3f;
			float[] still = Formation(PackTactic.Surround, Roles(3), Deg(0f, 120f, 240f));
			float[] turning = Formation(PackTactic.Kite, Roles(3), Deg(0f, 120f, 240f), kiteAdvance: advance);
			for (int i = 0; i < 3; ++i)
			{
				Assert.AreEqual(NPCGroup.Wrap(still[i] + advance), turning[i], 1e-3f,
					"the ring turns from wherever the members are, not from a stored angle they drifted from");
			}
		}

		[Test]
		public void FocusFire_ClustersOnTheSideThePackIsOn_NotOnTheWorldAxis()
		{
			float[] west = Formation(PackTactic.FocusFire, Roles(3), Deg(170f, 180f, 190f));
			float half = NPCGroup.CLUSTER_SPREAD * 0.5f;
			AssertAngles(new[] { Mathf.PI - half, Mathf.PI, Mathf.PI + half }, west,
				"a pack approaching from the west clusters in the west (it used to cluster at world +X)");

			float[] acrossTheSeam = Formation(PackTactic.FocusFire, Roles(3), Deg(350f, 0f, 10f));
			AssertAngles(new[] { NPCGroup.Wrap(-half), 0f, half }, acrossTheSeam,
				"bearings either side of 0 average to 0, not to 180");
		}

		[Test]
		public void Flank_PutsTheTankInFront_AndEveryoneElseBehind()
		{
			List<NPCGroupRole> roles = new List<NPCGroupRole> { NPCGroupRole.DPS, NPCGroupRole.Tank, NPCGroupRole.DPS };
			float front = 90f * Mathf.Deg2Rad;
			float[] angles = Formation(PackTactic.Flank, roles, Deg(100f, 90f, 80f), flankFront: front);

			Assert.AreEqual(front, angles[1], 1e-3f, "the tank holds the front");
			// Measured from the front: the member just left of it takes the left rear, the one just right the right rear.
			Assert.AreEqual(NPCGroup.Wrap(front + Mathf.PI * 0.5f), angles[0], 1e-3f);
			Assert.AreEqual(NPCGroup.Wrap(front + Mathf.PI * 1.5f), angles[2], 1e-3f);

			float[] lone = Formation(PackTactic.Flank, new List<NPCGroupRole> { NPCGroupRole.Tank, NPCGroupRole.DPS }, Deg(90f, 45f), flankFront: front);
			Assert.AreEqual(NPCGroup.Wrap(front + Mathf.PI), lone[1], 1e-3f,
				"a lone flanker goes directly behind (it used to stand at the side, 90° from the front)");
		}

		[Test]
		public void NoTactic_LeavesEveryoneWhereTheyStand()
		{
			AssertAngles(Deg(12f, 34f), Formation(PackTactic.None, Roles(2), Deg(12f, 34f)), "no tactic, no slots");
		}

		[Test]
		public void Wrap_KeepsAnglesInOneTurn()
		{
			Assert.AreEqual(0f, NPCGroup.Wrap(Mathf.PI * 2f), 1e-5f);
			Assert.AreEqual(Mathf.PI * 1.5f, NPCGroup.Wrap(-Mathf.PI * 0.5f), 1e-5f);
			Assert.That(NPCGroup.Wrap(-1e-9f), Is.GreaterThanOrEqualTo(0f).And.LessThan(Mathf.PI * 2f));
		}

		// --- Membership on live brains ---------------------------------------------------------------

		[Test]
		public void AddMember_BindsTheBrainWithItsRole_AndHandsThePackToTheBrainsHost()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);

			NPCGroup pack = new NPCGroup(PackTactic.Surround, true, 5f, 30f);
			Assert.IsTrue(pack.AddMember(a, NPCGroupRole.Tank));
			Assert.IsTrue(pack.AddMember(b, NPCGroupRole.DPS));

			Assert.AreSame(pack, a.Group);
			Assert.AreEqual(NPCGroupRole.Tank, a.GroupRole);
			Assert.AreEqual(2, pack.MemberCount);
			Assert.AreEqual(1, host.GroupCount, "the pack is ticked by its members' host, not by an Update of its own");

			Assert.IsTrue(pack.AddMember(a, NPCGroupRole.Support), "adding again changes the role");
			Assert.AreEqual(NPCGroupRole.Support, a.GroupRole);
			Assert.AreEqual(2, pack.MemberCount, "and does not add the brain twice");
		}

		[Test]
		public void ABrain_IsInOnePackAtATime()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);

			NPCGroup first = new NPCGroup(null);
			first.AddMember(a, NPCGroupRole.DPS);
			first.AddMember(b, NPCGroupRole.DPS);
			NPCGroup second = new NPCGroup(null);
			second.AddMember(a, NPCGroupRole.Tank);

			Assert.AreSame(second, a.Group);
			Assert.AreEqual(1, first.MemberCount, "joining a second pack leaves the first");
			Assert.AreEqual(2, host.GroupCount);
		}

		[Test]
		public void TheLastMemberLeaving_ReleasesThePack_AndAReleasedPackTakesNobody()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);

			NPCGroup pack = new NPCGroup(null);
			pack.AddMember(a, NPCGroupRole.DPS);
			pack.AddMember(b, NPCGroupRole.DPS);

			pack.RemoveMember(a);
			Assert.IsNull(a.Group);
			Assert.AreEqual(NPCGroupRole.None, a.GroupRole);
			Assert.IsFalse(pack.Released, "a pack with a member standing is not released");

			pack.RemoveMember(b);
			Assert.IsTrue(pack.Released);
			Assert.AreEqual(0, host.GroupCount, "a released pack stops ticking");
			Assert.IsFalse(pack.AddMember(a, NPCGroupRole.DPS), "and never takes a member again");
			Assert.IsNull(a.Group);
		}

		[Test]
		public void Death_Despawn_AndPoolReset_TakeABrainOutOfItsPack()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);

			NPCGroup pack = new NPCGroup(null);
			pack.AddMember(a, NPCGroupRole.DPS);
			pack.AddMember(b, NPCGroupRole.DPS);

			a.SuspendForCorpse();
			Assert.IsNull(a.Group, "a corpse leaves at death, not when its body decays");
			Assert.AreEqual(1, pack.MemberCount);

			host.RetireBrain(b);
			Assert.IsNull(b.Group, "a despawn resets the brain for the pool, and the reset leaves the pack");
			Assert.IsTrue(pack.Released, "the last member gone releases the pack");
		}

		[Test]
		public void ADestroyedBrain_IsPrunedAtTheNextEvaluation()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);

			NPCGroup pack = new NPCGroup(null);
			pack.AddMember(a, NPCGroupRole.DPS);
			pack.AddMember(b, NPCGroupRole.DPS);

			// In edit mode a MonoBehaviour's OnDestroy does not run, so this is the path left.
			UnityEngine.Object.DestroyImmediate(a.gameObject);
			pack.Evaluate(NPCGroup.EVALUATE_INTERVAL);

			Assert.AreEqual(1, pack.MemberCount);
			Assert.IsFalse(pack.Released);
		}

		// --- Spawner ownership ---------------------------------------------------------------------

		[Test]
		public void ASpawnerWithoutAPack_JoinsNobody()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);

			SpawnerDefinition definition = new SpawnerDefinition { Spawnables = new List<SpawnableSettings> { new NPCSpawnableSettings() } };
			Assert.IsFalse(definition.Pack.Enabled, "packs are off unless authored, so existing spawners run as they did");
			SpawnerRuntime runtime = new SpawnerRuntime(definition, default, null, new SpawnerScheduler());

			Assert.IsNull(runtime.JoinPack(a, definition.Spawnables[0]));
			Assert.IsNull(a.Group);
			Assert.IsNull(runtime.Pack);
			Assert.AreEqual(0, host.GroupCount);
		}

		[Test]
		public void APackSpawner_FoundsOnePack_WithItsTactic_AndEachEntrysRole()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);
			SpawnerRuntime runtime = PackRuntime(PackTactic.Flank, out NPCSpawnableSettings tank, out NPCSpawnableSettings dps);

			NPCGroup pack = runtime.JoinPack(a, tank);
			Assert.IsNotNull(pack);
			Assert.AreSame(pack, runtime.JoinPack(b, dps), "one spawner, one pack");
			Assert.AreSame(pack, runtime.Pack);
			Assert.AreEqual(PackTactic.Flank, pack.Tactic);
			Assert.AreEqual(NPCGroupRole.Tank, a.GroupRole);
			Assert.AreEqual(NPCGroupRole.DPS, b.GroupRole);
			Assert.AreEqual(1, host.GroupCount);
		}

		[Test]
		public void ARespawn_RejoinsItsPack_WhileAMemberStands_AndFoundsTheNextAfterAWipe()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out NPC aNpc);
			AIController b = PrepareOrc(host, out _);
			SpawnerRuntime runtime = PackRuntime(PackTactic.Surround, out NPCSpawnableSettings tank, out NPCSpawnableSettings dps);

			NPCGroup first = runtime.JoinPack(a, tank);
			runtime.JoinPack(b, dps);

			// a dies, its corpse decays back into the pool, and it respawns.
			a.SuspendForCorpse();
			host.RetireBrain(a);
			host.Prepare(aNpc, Vector3.zero);
			Assert.AreSame(first, runtime.JoinPack(a, tank), "a respawn rejoins the pack its packmate still holds");
			Assert.AreEqual(NPCGroupRole.Tank, a.GroupRole);
			Assert.AreEqual(2, first.MemberCount);

			// The whole pack falls.
			a.SuspendForCorpse();
			b.SuspendForCorpse();
			Assert.IsTrue(first.Released);
			Assert.IsNull(runtime.Pack);
			Assert.AreEqual(0, host.GroupCount);

			host.RetireBrain(a);
			host.Prepare(aNpc, Vector3.zero);
			NPCGroup second = runtime.JoinPack(a, tank);
			Assert.IsNotNull(second);
			Assert.AreNotSame(first, second, "after a wipe the next spawn founds a fresh pack");
			Assert.AreEqual(1, host.GroupCount);
		}

		[Test]
		public void SpawnObject_PutsTheSpawnedNpcInThePack()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out NPC aNpc);
			SpawnerRuntime runtime = PackRuntime(PackTactic.None, out _, out _);
			runtime.NetworkSpawnOverride = _ => aNpc;

			Assert.AreEqual(SpawnOutcome.Spawned, runtime.SpawnObject());
			Assert.IsNotNull(runtime.Pack);
			Assert.AreSame(runtime.Pack, a.Group, "the spawn path joins the NPC it made");
			Assert.AreEqual(NPCGroupRole.Tank, a.GroupRole, "with its entry's role (the first entry, spawning Linear)");
		}

		[Test]
		public void StoppingTheSpawner_DissolvesItsPack()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);
			SpawnerRuntime runtime = PackRuntime(PackTactic.None, out NPCSpawnableSettings tank, out NPCSpawnableSettings dps);
			NPCGroup pack = runtime.JoinPack(a, tank);
			runtime.JoinPack(b, dps);

			runtime.Stop();

			Assert.IsTrue(pack.Released);
			Assert.IsNull(a.Group);
			Assert.IsNull(b.Group);
			Assert.AreEqual(0, host.GroupCount);
		}

		// --- Ticking under the AI contract -------------------------------------------------------------

		[Test]
		public void APack_ThinksOnlyOnItsAiTick()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			NPCGroup pack = new NPCGroup(null);
			pack.AddMember(a, NPCGroupRole.DPS);

			List<int> evaluatedOn = new List<int>();
			for (int tick = 0; tick < 200; ++tick)
			{
				if (pack.Tick(NETWORK_TICK, TICKS_PER_AI_UPDATE))
				{
					evaluatedOn.Add(tick);
				}
			}

			Assert.That(evaluatedOn.Count, Is.GreaterThanOrEqualTo(3), "an Active pack evaluates about twice a second");
			for (int i = 1; i < evaluatedOn.Count; ++i)
			{
				Assert.AreEqual(0, (evaluatedOn[i] - evaluatedOn[0]) % TICKS_PER_AI_UPDATE,
					"a pack thinks on its AI tick, never between");
			}
		}

		[Test]
		public void AFarOrDormantPack_DoesNothing_AndStandsDown()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			NPCGroup pack = new NPCGroup(PackTactic.Surround, true, 5f, 30f);
			pack.AddMember(a, NPCGroupRole.DPS);

			// As if the last evaluation had handed out a tactic slot.
			a.SetPackSlot(1.25f);
			SetField(pack, "holdsSlots", true);

			for (int i = 0; i < 100; ++i)
			{
				Assert.IsFalse(pack.Advance(AILodTier.Far, 10, AI_TICK), "a Far pack never evaluates");
				Assert.IsFalse(pack.Advance(AILodTier.Dormant, 40, AI_TICK), "nor a Dormant one");
			}
			Assert.IsFalse(a.HasPackSlot, "the fight is over for a pack nobody is near; its slots go with it");
			Assert.IsNull(pack.GroupTargetCharacter);
			Assert.IsFalse(pack.IsInCombat);
		}

		[Test]
		public void ANearbyPack_EvaluatesAtItsLodInterval()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();

			int Count(AILodTier tier, int lodInterval)
			{
				AIController member = PrepareOrc(host, out _);
				NPCGroup pack = new NPCGroup(null);
				pack.AddMember(member, NPCGroupRole.DPS);
				int evaluations = 0;
				for (int i = 0; i < 120; ++i)
				{
					if (pack.Advance(tier, lodInterval, AI_TICK))
					{
						++evaluations;
					}
				}
				return evaluations;
			}

			// 120 AI ticks of 2/15 s is 16 s: every 4 AI ticks at Active, every 12 at Nearby (interval 3).
			int active = Count(AILodTier.Active, 1);
			int nearby = Count(AILodTier.Nearby, 3);
			Assert.That(active, Is.InRange(29, 31));
			Assert.That(nearby, Is.InRange(9, 11), "a Nearby pack evaluates a third as often, as its members think");
		}

		[Test]
		public void Tick_FollowsTheMostActiveRunningMember()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			AIBrainHost host = NewHost();
			AIController a = PrepareOrc(host, out _);
			AIController b = PrepareOrc(host, out _);
			NPCGroup pack = new NPCGroup(null);
			pack.AddMember(a, NPCGroupRole.DPS);
			pack.AddMember(b, NPCGroupRole.DPS);

			SetField(a, "currentLodTier", AILodTier.Dormant);
			SetField(b, "currentLodTier", AILodTier.Far);
			Assert.AreEqual(0, CountTicks(pack, 300), "nobody in a fighting tier: the pack does nothing");

			SetField(b, "currentLodTier", AILodTier.Nearby);
			Assert.That(CountTicks(pack, 300), Is.GreaterThan(0), "one member in reach of a player wakes the pack");

			// A quarantined brain does not count, whatever its tier.
			SetField(b, "currentLodTier", AILodTier.Active);
			b.Quarantine();
			Assert.AreEqual(0, CountTicks(pack, 300), "only running brains set the pack's tier");
		}

		// --- Wiring --------------------------------------------------------------------------------

		[Test]
		public void NPCGroup_IsAPlainObject_WithNoUpdateOfItsOwn()
		{
			Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(typeof(NPCGroup)),
				"a pack is created by its spawner at runtime, not placed in a scene");
			Assert.IsNull(typeof(NPCGroup).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
				"the brain host ticks packs; a pack must not run its own Update");

			string host = SourceScanPins.ReadCode(AI_ROOT + "AIBrainHost.cs");
			SourceScanPins.HoldsAndFires("AIBrainHost.OnTick", host,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private void OnTick()"), "brain.Tick()", "TickGroups();"),
				SourceScanPins.Replace("TickGroups();", string.Empty),
				"packs no longer ticked by the host");
		}

		[Test]
		public void TheBrain_AlertsItsPack_AndLeavesItOnDeathDespawnAndDestroy()
		{
			string brain = SourceScanPins.ReadCode(AI_ROOT + "AIController.cs");

			SourceScanPins.HoldsAndFires("ChangeState", brain,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "public void ChangeState(BaseAIState newState"),
					"ICharacter engaged = TargetCharacter;", "Group.AlertGroup(engaged);"),
				SourceScanPins.Replace("Group.AlertGroup(engaged);", string.Empty),
				"entering combat no longer alerts the pack");

			foreach (string signature in new[] { "public bool SuspendForCorpse()", "internal void ResetForPool()", "private void OnDestroying()" })
			{
				SourceScanPins.HoldsAndFires(signature, brain,
					code => (SourceScanPins.Body(code, signature) ?? string.Empty).Contains("LeavePack();") ? null : "does not leave the pack",
					code => code.Replace(SourceScanPins.Body(code, signature), SourceScanPins.Body(code, signature).Replace("LeavePack();", string.Empty)),
					$"{signature} keeps its pack");
			}
		}

		[Test]
		public void TheSpawner_JoinsAfterTheNetworkSpawn()
		{
			string runtime = SourceScanPins.ReadCode("Assets/Scripts/Server/Implementation/World/SceneServer/Spawner/SpawnerRuntime.cs");
			SourceScanPins.HoldsAndFires("SpawnObject", runtime,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "public SpawnOutcome SpawnObject()"),
					"brain = brainHost.Prepare(", "NetworkManager.ServerManager.Spawn(nob, null, Scene);", "JoinPack(brain, spawnableSettings);", "completed = true;"),
				SourceScanPins.Replace("JoinPack(brain, spawnableSettings);", string.Empty),
				"a spawned NPC never joins its spawner's pack");
		}

		[Test]
		public void BossAdds_ComeFromThePool_IntoTheBossesScene_AsItsPack()
		{
			string boss = SourceScanPins.ReadCode(AI_ROOT + "Boss/BossScriptState.cs");

			Assert.IsFalse(boss.Contains("Object.Instantiate("), "an add is drawn from the pool, not instantiated");
			SourceScanPins.HoldsAndFires("SpawnAdd", boss,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private static AIController SpawnAdd("),
					"networkManager.GetPooledInstantiated(prefab, position, rotation, true);",
					"MoveGameObjectToScene(instance.gameObject, scene);",
					"host.Prepare(npc, position);",
					"networkManager.ServerManager.Spawn(instance, null, scene);"),
				SourceScanPins.Replace("networkManager.ServerManager.Spawn(instance, null, scene);", "networkManager.ServerManager.Spawn(instance);"),
				"adds spawned into the active scene instead of the boss's");
			SourceScanPins.HoldsAndFires("SpawnAdds", boss,
				code => SourceScanPins.InOrder(SourceScanPins.Body(code, "private void SpawnAdds("),
					"pack = ResolveAddPack(controller);", "pack.AddMember(add, NPCGroupRole.DPS);", "pack.AlertGroup(enemy);"),
				SourceScanPins.Replace("pack.AddMember(add, NPCGroupRole.DPS);", string.Empty),
				"adds no longer join the boss's pack");
		}

		[Test]
		public void PackAuthoring_IsServerOnly_AndOffByDefault()
		{
			Assert.AreEqual("FishMMO.Server", typeof(NPCPackSettings).Assembly.GetName().Name,
				"pack data rides the server-only spawn tables and must not compile into a client");
			Assert.IsFalse(new NPCPackSettings().Enabled);
			Assert.IsFalse(new SpawnerDefinition().Pack.Enabled);
			Assert.AreEqual(NPCGroupRole.DPS, new NPCSpawnableSettings().PackRole, "a new entry is a DPS member unless authored otherwise");
		}

		// --- Helpers -------------------------------------------------------------------------------

		private static AIBrainHost NewHost()
		{
			AIBrainCatalogue catalogue = AssetDatabase.LoadAssetAtPath<AIBrainCatalogue>(BrainCatalogueYaml.CataloguePath);
			Assert.IsNotNull(catalogue, $"no brain catalogue at {BrainCatalogueYaml.CataloguePath}");
			catalogue.Invalidate();
			return new AIBrainHost(null, catalogue);
		}

		/// <summary>
		/// An edit-mode orc, prepared by <paramref name="host"/>, with the references
		/// <c>BaseCharacter.Awake</c> would have set.
		/// </summary>
		private AIController PrepareOrc(AIBrainHost host, out NPC npc)
		{
			GameObject instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ORC));
			created.Add(instance);
			npc = instance.GetComponent<NPC>();
			BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.GameObject), flags).SetValue(npc, instance);
			typeof(BaseCharacter).GetProperty(nameof(BaseCharacter.Transform), flags).SetValue(npc, instance.transform);

			AIController brain = host.Prepare(npc, Vector3.zero);
			Assert.IsNotNull(brain);
			Assert.IsTrue(brain.IsRunning);
			return brain;
		}

		/// <summary>
		/// A runtime whose pack is on, with a Tank entry and a DPS entry, spawning Linear.
		/// </summary>
		private static SpawnerRuntime PackRuntime(PackTactic tactic, out NPCSpawnableSettings tank, out NPCSpawnableSettings dps)
		{
			tank = new NPCSpawnableSettings { PackRole = NPCGroupRole.Tank };
			dps = new NPCSpawnableSettings { PackRole = NPCGroupRole.DPS };
			SpawnerDefinition definition = new SpawnerDefinition
			{
				Name = "Pack",
				MaxSpawnCount = 2,
				SpawnType = ObjectSpawnType.Linear,
				Spawnables = new List<SpawnableSettings> { tank, dps },
				Pack = new NPCPackSettings { Enabled = true, Tactic = tactic },
			};
			return new SpawnerRuntime(definition, default, null, new SpawnerScheduler());
		}

		private static int CountTicks(NPCGroup pack, int ticks)
		{
			int evaluations = 0;
			for (int i = 0; i < ticks; ++i)
			{
				if (pack.Tick(NETWORK_TICK, TICKS_PER_AI_UPDATE))
				{
					++evaluations;
				}
			}
			return evaluations;
		}

		private static void SetField(object target, string name, object value)
		{
			FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(field, $"{target.GetType().Name}.{name} not found; re-anchor the test");
			field.SetValue(target, value);
		}

		private static List<NPCGroupRole> Roles(int count)
		{
			List<NPCGroupRole> roles = new List<NPCGroupRole>(count);
			for (int i = 0; i < count; ++i)
			{
				roles.Add(NPCGroupRole.DPS);
			}
			return roles;
		}

		private static float[] Deg(params float[] degrees)
		{
			float[] radians = new float[degrees.Length];
			for (int i = 0; i < degrees.Length; ++i)
			{
				radians[i] = NPCGroup.Wrap(degrees[i] * Mathf.Deg2Rad);
			}
			return radians;
		}

		private static float[] Formation(PackTactic tactic, List<NPCGroupRole> roles, float[] bearings, float flankFront = 0f, float kiteAdvance = 0f)
		{
			List<float> angles = new List<float>();
			NPCGroup.AssignFormation(tactic, roles, bearings, flankFront, kiteAdvance, new List<int>(), angles);
			Assert.AreEqual(bearings.Length, angles.Count, "one angle per member");
			return angles.ToArray();
		}

		private static void AssertAngles(float[] expected, float[] actual, string because)
		{
			for (int i = 0; i < expected.Length; ++i)
			{
				float difference = Mathf.Abs(Mathf.DeltaAngle(expected[i] * Mathf.Rad2Deg, actual[i] * Mathf.Rad2Deg));
				Assert.That(difference, Is.LessThan(0.1f), $"member {i}: expected {expected[i] * Mathf.Rad2Deg:F2}°, got {actual[i] * Mathf.Rad2Deg:F2}° — {because}");
			}
		}
	}
}
