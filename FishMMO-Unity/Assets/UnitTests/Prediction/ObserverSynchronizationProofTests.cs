using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// End-to-end proof that a non-owning client's copy of a character actually converges on the
	/// server's state through the interpolated (forwarding-off) observer channels.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The existing observer fixtures prove the <i>pieces</i>: that each message round-trips, that
	/// the owner is excluded from the recipient set, that the LOD picks the right band, that the
	/// push schedulers gate correctly. None of them proves the thing that actually matters — that a
	/// message arriving at an observer <b>mutates that observer's controller state to match the
	/// server's</b>. That is what this fixture asserts, for each of the four subsystems an observer
	/// renders: attributes, resources, buffs and equipment.
	/// </para>
	/// <para>
	/// Every test drives the <b>whole chain</b>: build the server-side value, serialise it with the
	/// production serializer, read it back through the production reader, hand the result to the
	/// production apply method, then assert on the observer controller's public state. A test that
	/// only called the apply method would not catch a serializer that dropped a field.
	/// </para>
	/// <para>
	/// An "observer" here is a controller whose <c>NetworkObject</c> is unspawned or not owned
	/// locally — which is exactly what the controllers read as "not the server, not the owner".
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObserverSynchronizationProofTests
	{
		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

		private const int HealthTemplateID = 9101;
		private const int ManaTemplateID = 9102;
		private const int StaminaTemplateID = 9103;

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] serializerTypes =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};
			foreach (Type t in serializerTypes)
			{
				MethodInfo register = t.GetMethod("RegisterSerializers",
					BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				LogAssert.IsNotNull(register, $"{t.Name} must expose RegisterSerializers.");
				register.Invoke(null, null);
			}
		}

		[TearDown]
		public void TearDown()
		{
			foreach (UnityEngine.Object a in assets)
			{
				if (a is ICachedObject c) c.RemoveFromCache();
				if (a != null) UnityEngine.Object.DestroyImmediate(a);
			}
			assets.Clear();
			foreach (GameObject go in gameObjects)
			{
				if (go != null) UnityEngine.Object.DestroyImmediate(go);
			}
			gameObjects.Clear();
		}

		// ── ATTRIBUTES ───────────────────────────────────────────────────────

		/// <summary>
		/// A changed-attribute broadcast reaches an observer and lands on both the base value and
		/// the external modifier, leaving every attribute it does not name untouched.
		/// </summary>
		/// <remarks>
		/// The external modifier is the half that used to be missing. A character standing in
		/// +max-health gear showed its <i>unbuffed</i> maximum to every observer, because only the
		/// base value travelled — so this asserts <c>FinalValue</c>, not just <c>Value</c>.
		/// </remarks>
		[Test]
		public void Attributes_ChangedEntriesReachTheObserver_AndLeaveOthersAlone()
		{
			CharacterAttributeController observer = MakeAttributeController("ObserverAttrs", out int[] ids);

			int untouchedID = ids[2];
			int untouchedValue = observer.Attributes[untouchedID].Value;

			// Server side: two attributes move — one base value, one external modifier.
			AttributeReconcileEntry[] changed =
			{
				new AttributeReconcileEntry { TemplateID = ids[0], Value = 77, ExternalModifier = 0 },
				new AttributeReconcileEntry { TemplateID = ids[1], Value = 31, ExternalModifier = 44 },
			};

			CharacterAttributesBroadcast received = RoundTripAttributes(new CharacterAttributesBroadcast
			{
				CharacterObjectID = 1234,
				IsFullSet = false,
				Attributes = changed,
			});

			observer.ApplyObservedAttributes(received.Attributes);

			LogAssert.AreEqual(77, observer.Attributes[ids[0]].Value,
				"The observer must hold the server's base value after the broadcast.");
			LogAssert.AreEqual(31, observer.Attributes[ids[1]].Value,
				"The observer must hold the server's base value for the second changed attribute.");
			LogAssert.AreEqual(44, observer.Attributes[ids[1]].ExternalModifier,
				"The external modifier must travel; without it an observer computes the wrong FinalValue.");
			LogAssert.AreEqual(31 + 44, observer.Attributes[ids[1]].FinalValue,
				"FinalValue is what every derived formula reads — base plus modifier must both arrive.");
			LogAssert.AreEqual(untouchedValue, observer.Attributes[untouchedID].Value,
				"An attribute the message does not name means 'unchanged', never 'reset'.");
		}

		/// <summary>The first push carries the whole sheet, and every entry of it lands.</summary>
		[Test]
		public void Attributes_FullSetReachesTheObserver_Entirely()
		{
			CharacterAttributeController observer = MakeAttributeController("ObserverFullSet", out int[] ids);

			AttributeReconcileEntry[] sheet = new AttributeReconcileEntry[ids.Length];
			for (int i = 0; i < ids.Length; ++i)
			{
				sheet[i] = new AttributeReconcileEntry { TemplateID = ids[i], Value = 500 + i, ExternalModifier = i };
			}

			CharacterAttributesBroadcast received = RoundTripAttributes(new CharacterAttributesBroadcast
			{
				CharacterObjectID = 1234,
				IsFullSet = true,
				Attributes = sheet,
			});

			LogAssert.IsTrue(received.IsFullSet, "The full-set flag must survive the wire.");
			observer.ApplyObservedAttributes(received.Attributes);

			for (int i = 0; i < ids.Length; ++i)
			{
				LogAssert.AreEqual(500 + i, observer.Attributes[ids[i]].Value,
					$"Attribute {i} of the full sheet must reach the observer.");
				LogAssert.AreEqual(i, observer.Attributes[ids[i]].ExternalModifier,
					$"Attribute {i}'s modifier must reach the observer.");
			}
		}

		// ── RESOURCES ────────────────────────────────────────────────────────

		/// <summary>
		/// A resource broadcast reaches an observer and moves the bars it is drawn from.
		/// </summary>
		[Test]
		public void Resources_ReachTheObserver_AndMoveEveryBar()
		{
			CharacterAttributeController observer = MakeAttributeController("ObserverResources", out _);

			CharacterResourcesBroadcast sent = new CharacterResourcesBroadcast
			{
				CharacterObjectID = 1234,
				Health = 812, MaxHealth = 1200,
				Mana = 240, MaxMana = 800,
				Stamina = 310, MaxStamina = 400,
			};

			CharacterResourcesBroadcast received = RoundTripResources(sent);

			observer.ApplyObservedResourceState(
				received.Health, received.MaxHealth,
				received.Mana, received.MaxMana,
				received.Stamina, received.MaxStamina);

			LogAssert.IsTrue(observer.TryGetHealthAttribute(out CharacterResourceAttribute health),
				"The probe controller must expose a health resource.");
			LogAssert.IsTrue(observer.TryGetManaAttribute(out CharacterResourceAttribute mana), "Mana resource missing.");
			LogAssert.IsTrue(observer.TryGetStaminaAttribute(out CharacterResourceAttribute stamina), "Stamina resource missing.");

			LogAssert.AreEqual(812f, health.CurrentValue,
				"An observer's health bar is drawn from CurrentValue; the broadcast must set it.");
			/* FinalValue, not Value. ApplyIndividualResourceState calls SetFinal rather than
			 * SetValue precisely because the server's maximum is authoritative — running the local
			 * formula would overwrite it with a locally computed result. FinalValue is what the bar
			 * is drawn against. */
			LogAssert.AreEqual(1200, health.FinalValue,
				"The maximum must arrive too, or the bar is drawn against the wrong denominator.");
			LogAssert.AreEqual(800, mana.FinalValue, "Mana maximum must reach the observer.");
			LogAssert.AreEqual(400, stamina.FinalValue, "Stamina maximum must reach the observer.");

			LogAssert.AreEqual(240f, mana.CurrentValue, "Mana must reach the observer.");
			LogAssert.AreEqual(310f, stamina.CurrentValue, "Stamina must reach the observer.");
		}

		/// <summary>
		/// The whole-unit wire form is what the change gate compares at, so a value that survives
		/// the round trip is exactly the value the sender decided to send.
		/// </summary>
		[Test]
		public void Resources_WholeUnitFormIsLosslessAgainstItsOwnChangeGate()
		{
			CharacterAttributeResourceState a = new CharacterAttributeResourceState
			{
				Health = 812.4f, MaxHealth = 1200, Mana = 240.6f, MaxMana = 800,
				Stamina = 310.5f, MaxStamina = 400, NextRegenTick = 0u,
			};
			CharacterAttributeResourceState rounded = new CharacterAttributeResourceState
			{
				Health = Mathf.RoundToInt(a.Health), MaxHealth = a.MaxHealth,
				Mana = Mathf.RoundToInt(a.Mana), MaxMana = a.MaxMana,
				Stamina = Mathf.RoundToInt(a.Stamina), MaxStamina = a.MaxStamina,
				NextRegenTick = 0u,
			};

			LogAssert.IsFalse(
				ObservedResourcePushScheduler.ResourcesDifferForObservers(a, rounded),
				"Rounding to whole units must not itself register as a change, or the gate would " +
				"push every interval forever on sub-unit regeneration drift.");
		}

		// ── BUFFS ────────────────────────────────────────────────────────────

		/// <summary>
		/// A buff broadcast reaches an observer's display list, and a later one that drops a buff
		/// removes it — proving the observer converges rather than only accumulating.
		/// </summary>
		[Test]
		public void Buffs_ReachTheObserver_AndConvergeOnRemoval()
		{
			BuffController observer = MakeBuffController("ObserverBuffs");

			ObservedBuffEntry[] two =
			{
				new ObservedBuffEntry { TemplateID = 700, Stacks = 2, RemainingSeconds = 12.5f, TotalSeconds = 30f },
				new ObservedBuffEntry { TemplateID = 701, Stacks = 0, RemainingSeconds = 4f, TotalSeconds = 10f },
			};

			ApplyObservedBuffs(observer, two);

			LogAssert.AreEqual(2, observer.ObservedBuffs.Count,
				"Both visible buffs must reach the observer.");
			LogAssert.AreEqual(700, observer.ObservedBuffs[0].TemplateID, "Template id must survive.");
			LogAssert.AreEqual(2, observer.ObservedBuffs[0].Stacks, "Stack count must survive.");
			LogAssert.AreEqual(12.5f, observer.ObservedBuffs[0].RemainingSeconds, "Remaining seconds must survive.");

			// The second push drops one buff. The observer must end up with the server's set,
			// not the union of both pushes.
			ApplyObservedBuffs(observer, new[] { two[1] });

			LogAssert.AreEqual(1, observer.ObservedBuffs.Count,
				"A push that omits a buff must remove it — an observer converges on the server's set.");
			LogAssert.AreEqual(701, observer.ObservedBuffs[0].TemplateID,
				"The surviving buff must be the one the server still lists.");

			// And an empty push clears it entirely.
			ApplyObservedBuffs(observer, System.Array.Empty<ObservedBuffEntry>());
			LogAssert.AreEqual(0, observer.ObservedBuffs.Count, "An empty push must clear the display list.");
		}

		/// <summary>
		/// An observer never builds simulation state from a buff broadcast — no <c>Buff</c>, no
		/// attribute modifier on somebody else's character.
		/// </summary>
		/// <remarks>
		/// This is the regression that produced "a poison that killed its victim was still slowing
		/// them, on every onlooker's screen, for as long as they stayed in view".
		/// </remarks>
		[Test]
		public void Buffs_ObserverBuildsDisplayStateOnly_NeverSimulation()
		{
			BuffController observer = MakeBuffController("ObserverBuffsDisplayOnly");

			ApplyObservedBuffs(observer, new[]
			{
				new ObservedBuffEntry { TemplateID = 700, Stacks = 3, RemainingSeconds = 12.5f, TotalSeconds = 30f },
			});

			LogAssert.AreEqual(1, observer.ObservedBuffs.Count, "The display list must be populated.");
			LogAssert.AreEqual(0, observer.Buffs.Count,
				"An observer must hold NO simulated Buff for a peer. A Buff carries attribute " +
				"modifiers and an expiry in the owner's tick domain; an observer never ticks it, so " +
				"one written here would apply its modifiers forever.");
		}

		// ── EQUIPMENT ────────────────────────────────────────────────────────

		/// <summary>
		/// An observed-slot broadcast reaches an observer, fills the slot with the template and
		/// seed a mesh is chosen from, and a later empty message clears it.
		/// </summary>
		[Test]
		public void Equipment_SlotChangesReachTheObserver_AndClearAgain()
		{
			EquipmentController observer = MakeEquipmentController("ObserverEquipment");

			BaseItemTemplate template = MakeItemTemplate("ProofSword");

			/* Seed 0: the probe template is not marked Generate, so the Item it produces has no
			 * ItemGenerator and reports its seed as 0. ApplyObservedSlot's idempotence test reads
			 * `current.IsGenerated ? current.Generator.Seed : 0`, so a non-zero seed here would
			 * never match and every re-apply would rebuild — an artefact of the probe, not of the
			 * production path. That the seed FIELD survives the wire is asserted separately. */
			EquipmentObservedSlotBroadcast received = RoundTripEquipmentSlot(new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 1234,
				Slot = (byte)ItemSlot.Primary,
				TemplateID = template.ID,
				Seed = 0,
			});

			// The seed is what picks a generated item's appearance; prove it crosses the wire.
			EquipmentObservedSlotBroadcast seeded = RoundTripEquipmentSlot(new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 1234, Slot = (byte)ItemSlot.Primary, TemplateID = template.ID, Seed = 4242,
			});
			LogAssert.AreEqual(4242, seeded.Seed, "The generation seed must survive the wire.");
			LogAssert.AreEqual(template.ID, seeded.TemplateID, "The template id must survive the wire.");
			LogAssert.AreEqual((byte)ItemSlot.Primary, seeded.Slot, "The slot must survive the wire.");

			LogAssert.IsFalse(received.IsEmpty, "A message naming a template is not an empty-slot message.");
			ApplyObservedSlot(observer, received);

			LogAssert.IsTrue(observer.TryGetItem((int)ItemSlot.Primary, out Item equipped),
				"The observer's slot must be filled after the broadcast.");
			LogAssert.IsNotNull(equipped, "The slot must hold a real item.");
			LogAssert.AreEqual(template.ID, equipped.Template.ID,
				"The template is what EquipmentVisualController picks a mesh from; it must arrive.");

			// Applying the identical message again must be a no-op, which is what makes the spawn
			// payload and a broadcast that raced it safe to apply in either order.
			Item first = equipped;
			ApplyObservedSlot(observer, received);
			observer.TryGetItem((int)ItemSlot.Primary, out Item second);
			LogAssert.AreSame(first, second,
				"Re-applying the same template and seed must not rebuild the item, or the visual " +
				"controller would redraw on every duplicate.");

			// Emptying the slot.
			EquipmentObservedSlotBroadcast cleared = RoundTripEquipmentSlot(new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 1234,
				Slot = (byte)ItemSlot.Primary,
				TemplateID = 0,
				Seed = 0,
			});
			LogAssert.IsTrue(cleared.IsEmpty, "A zero template id is the empty-slot signal.");
			ApplyObservedSlot(observer, cleared);

			observer.TryGetItem((int)ItemSlot.Primary, out Item afterClear);
			LogAssert.IsNull(afterClear, "An empty-slot message must clear the observer's slot.");
		}

		// ── ABILITIES ────────────────────────────────────────────────────────

		/// <summary>
		/// The cast tuple an observer reproduces from survives the wire in every spawn mode,
		/// including the fields a mode omits coming back at the defaults the handler expects.
		/// </summary>
		[Test]
		public void Abilities_EveryCastReachesAnObserverIntact()
		{
			foreach (AbilitySpawnTarget mode in Enum.GetValues(typeof(AbilitySpawnTarget)))
			{
				AbilityActivatedBroadcast sent = new AbilityActivatedBroadcast
				{
					CasterObjectID = 40,
					AbilityID = 8_842_001_337L,
					Seed = -1_713_468_379,
					SpawnTick = 123_450u,
					ServerTick = 123_456u,
					SpawnMode = (byte)mode,
					TargetObjectID = 77,
					AimOrigin = new Vector3(112.5f, 32.6f, -47.2f),
					PackedAimDirection = AimDirectionCompression.Encode(new Vector3(0.3f, -0.1f, 0.95f)),
					SpawnPosition = new Vector3(113.1f, 32.6f, -46.4f),
					SpawnRotation = Quaternion.Euler(12f, 200f, 0f),
				};

				Writer w = new Writer();
				w.WriteAbilityActivatedBroadcast(sent);
				AbilityActivatedBroadcast got = new Reader(w.GetArraySegment(), null).ReadAbilityActivatedBroadcast();

				// The reproduction is a pure function of these four. If any of them changes on the
				// wire the observer's object is not the server's object.
				LogAssert.AreEqual(sent.CasterObjectID, got.CasterObjectID, $"[{mode}] caster id");
				LogAssert.AreEqual(sent.AbilityID, got.AbilityID, $"[{mode}] ability id");
				LogAssert.AreEqual(sent.Seed, got.Seed, $"[{mode}] seed — the whole basis of the reproduction");
				LogAssert.AreEqual(sent.SpawnTick, got.SpawnTick, $"[{mode}] spawn tick");
				LogAssert.AreEqual(sent.ServerTick, got.ServerTick, $"[{mode}] server tick — the fast-forward basis");
				LogAssert.AreEqual(sent.TargetObjectID, got.TargetObjectID, $"[{mode}] server-resolved target");

				// The container an observer files the object under must be the same id the server
				// used, or AbilityObjectDestroyedBroadcast cannot name it.
				LogAssert.AreEqual(
					AbilityContainerAllocator.ComputeContainerId(sent.Seed, new PredictionTick(sent.SpawnTick)),
					AbilityContainerAllocator.ComputeContainerId(got.Seed, new PredictionTick(got.SpawnTick)),
					$"[{mode}] the container id must be identical on both peers, or " +
					"AbilityObjectDestroyedBroadcast cannot name the object it ended.");

				if (mode == AbilitySpawnTarget.Camera)
				{
					LogAssert.AreEqual(sent.PackedAimDirection, got.PackedAimDirection,
						"[Camera] the aim is what the pose is re-derived from.");
					LogAssert.AreEqual(sent.AimOrigin, got.AimOrigin, "[Camera] aim origin");
				}
				else
				{
					LogAssert.AreEqual(sent.SpawnPosition, got.SpawnPosition, $"[{mode}] spawn position");
					float angle = Quaternion.Angle(sent.SpawnRotation, got.SpawnRotation);
					LogAssert.IsTrue(angle < 0.05f,
						$"[{mode}] spawn rotation must survive 64-bit packing to well under a tenth " +
						$"of a degree; measured {angle:F4}.");
				}
			}
		}

		// ── MODE GUARDS ──────────────────────────────────────────────────────

		/// <summary>
		/// With forwarding off, a non-owner must not apply the reconcile for any controller — the
		/// broadcast channels own that state.
		/// </summary>
		/// <remarks>
		/// In Mode A the reconcile never reaches a non-owner, so this asserts the stated contract
		/// rather than a reachable failure. It becomes load-bearing the moment forwarding is turned
		/// on for a scene: without it, <see cref="CooldownController"/> would apply a peer's exact
		/// cooldowns, which the spawn payload deliberately withholds.
		/// </remarks>
		[Test]
		public void ModeGuards_NonOwnerIgnoresReconcile_WhenForwardingIsOff()
		{
			CooldownController cooldowns = MakeGuardedCooldownController("GuardCooldownsOff", forwarding: false);

			CharacterReconcileData rd = default;
			rd.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };

			cooldowns.OnReconcile(rd, Channel.Unreliable);

			LogAssert.AreEqual(0, cooldowns.Cooldowns.Count,
				"With forwarding off, a non-owner must not take cooldowns from the reconcile. " +
				"AbilityController.WritePayload withholds them from observers for the same reason.");
		}

		/// <summary>With forwarding on, the same non-owner must apply it — that is the mode.</summary>
		[Test]
		public void ModeGuards_NonOwnerAppliesReconcile_WhenForwardingIsOn()
		{
			CooldownController cooldowns = MakeGuardedCooldownController("GuardCooldownsOn", forwarding: true);

			CharacterReconcileData rd = default;
			rd.Cooldowns = new[] { new CooldownReconcileEntry { AbilityID = 42, StartTick = 100, DurationTicks = 60 } };

			cooldowns.OnReconcile(rd, Channel.Unreliable);

			LogAssert.AreEqual(1, cooldowns.Cooldowns.Count,
				"With forwarding on the reconcile IS the observer's channel; the guard must let it through.");
		}

		/// <summary>
		/// Every reconcile consumer that mutates observer-visible state consults
		/// <see cref="ObserverSyncMode"/>, so the switch has no exceptions to remember.
		/// </summary>
		/// <remarks>
		/// A source scan rather than a behavioural test, because the point is the <i>absence</i> of
		/// an unguarded consumer — including one added later. <c>AbilityController</c> is exempt: it
		/// splits owner-only rollback from the authoritative fields every peer takes, and states so
		/// at its own <c>IsOwner</c> branch.
		/// </remarks>
		[Test]
		public void ModeGuards_EveryReconcileConsumerConsultsObserverSyncMode()
		{
			(string file, string reason)[] mustGuard =
			{
				("KCC/KCCPlayer.cs", "applying a motor state would give the transform two writers"),
				("Buff/BuffController.cs", "two writers spawn two effect instances for one buff"),
				("Ability/Cooldown/CooldownController.cs", "the spawn payload withholds a peer's cooldowns"),
				("Equipment/EquipmentController.cs", "two writers put a phantom item in one slot"),
				("CharacterAttribute/CharacterAttributeController.cs", "the broadcast carries the same fields at a coarser precision"),
			};

			foreach ((string file, string reason) in mustGuard)
			{
				string path = Path.Combine(
					Directory.GetCurrentDirectory(),
					"Assets/Scripts/Shared/Implementation/Entity/Prediction", file);
				LogAssert.IsTrue(File.Exists(path), $"{file} not found at {path}.");

				string source = File.ReadAllText(path);
				int reconcile = source.IndexOf("public void OnReconcile(", StringComparison.Ordinal);
				LogAssert.IsTrue(reconcile >= 0, $"{file} must implement OnReconcile.");

				// Brace-match the method body rather than guessing a window: the guard sits behind
				// a comment block whose length varies per controller.
				string body = ExtractMethodBody(source, reconcile);
				LogAssert.IsTrue(
					body.Contains("ObserverSyncMode.ObserversConsumeReconcile"),
					$"{file}.OnReconcile must open with the non-owner guard — {reason}. " +
					"See ObserverSyncMode: every reconcile consumer asks it whose turn it is.");
			}
		}

		// ── PREDICTED SCENE OBJECTS ──────────────────────────────────────────

		/// <summary>
		/// A predicted platform seeds a late joiner from its spawn payload, which is what lets it
		/// run with state forwarding off.
		/// </summary>
		/// <remarks>
		/// With forwarding off a scene object has no owner to reconcile to — <c>Reconcile_Send</c>
		/// returns immediately when <c>!Owner.IsValid &amp;&amp; !stateForwarding</c> — so nothing
		/// corrects a client that arrives mid-cycle. Per-tick movement needs no wire because
		/// <c>PerformReplicate</c> is autonomous and deterministic; only the starting point does.
		/// </remarks>
		[Test]
		public void PredictedPlatform_SeedsALateJoinerFromItsSpawnPayload()
		{
			GameObject go = new GameObject("PlatformProbe");
			gameObjects.Add(go);
			KCCPlatform platform = go.AddComponent<KCCPlatform>();

			// Awake does not run on AddComponent in EditMode; stand in for what it builds.
			SetPrivate(platform, "goals", new List<Vector3>
			{
				new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f),
			});
			platform.ID = -42L;
			go.transform.position = new Vector3(7.25f, 3f, -1.5f);
			SetPrivate(platform, "goalIndex", (byte)2);

			Writer writer = new Writer();
			platform.WritePayload(null, writer);

			Reader reader = new Reader(writer.GetArraySegment(), null);
			LogAssert.AreEqual(-42L, reader.ReadInt64(), "The scene-object id must still lead the payload.");
			LogAssert.AreEqual(new Vector3(7.25f, 3f, -1.5f), reader.ReadVector3(),
				"The platform's live position must travel, or a late joiner starts a lap out of phase.");
			LogAssert.AreEqual((byte)2, reader.ReadUInt8Unpacked(),
				"The goal index must travel, or the receiver heads for the wrong waypoint.");

			TestContext.WriteLine($"MEASURE platform spawn payload = {writer.Length} B, once per observer");

			// And the reader puts it back where the writer had it.
			GameObject lateGo = new GameObject("PlatformLateJoiner");
			gameObjects.Add(lateGo);
			KCCPlatform late = lateGo.AddComponent<KCCPlatform>();
			SetPrivate(late, "goals", new List<Vector3>
			{
				new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f),
			});
			try
			{
				late.ReadPayload(null, new Reader(writer.GetArraySegment(), null));
				LogAssert.AreEqual(new Vector3(7.25f, 3f, -1.5f), lateGo.transform.position,
					"The late joiner must start from the server's position.");
				LogAssert.AreEqual((byte)2, GetPrivate<byte>(late, "goalIndex"),
					"The late joiner must head for the server's waypoint.");
			}
			finally
			{
				SceneObject.Unregister(late);
			}
		}

		/// <summary>
		/// A goal index the local route cannot address restarts the cycle rather than throwing.
		/// </summary>
		/// <remarks>
		/// The waypoints come from the scene asset; a client whose asset disagrees with the server's
		/// would otherwise index past the end of <c>goals</c> inside the replicate on the very next
		/// tick. Wrong by at most one leg beats an exception in the tick loop.
		/// </remarks>
		[Test]
		public void PredictedPlatform_OutOfRangeGoalIndex_RestartsTheCycle()
		{
			GameObject go = new GameObject("PlatformShortRoute");
			gameObjects.Add(go);
			KCCPlatform platform = go.AddComponent<KCCPlatform>();
			SetPrivate(platform, "goals", new List<Vector3> { Vector3.zero, Vector3.one });

			Writer writer = new Writer();
			writer.WriteInt64(-7L);
			writer.WriteVector3(new Vector3(1f, 2f, 3f));
			writer.WriteUInt8Unpacked(9); // beyond a two-waypoint route

			try
			{
				platform.ReadPayload(null, new Reader(writer.GetArraySegment(), null));
				LogAssert.AreEqual((byte)0, GetPrivate<byte>(platform, "goalIndex"),
					"An unaddressable goal index must fall back to the start of the route.");
			}
			finally
			{
				SceneObject.Unregister(platform);
			}
		}

		// ── MODE DETECTION AT INITIALISATION ─────────────────────────────────

		/// <summary>
		/// The transport mode is decided from the inspector's <c>_enableStateForwarding</c> at
		/// initialisation, and the rule needs BOTH inputs.
		/// </summary>
		/// <remarks>
		/// An NPC keeps its <c>NetworkTransform</c> in either mode: prediction does not move it, so
		/// its <c>MotorState</c> is default every tick and the transform is the only thing carrying
		/// it anywhere. Silencing it because forwarding happened to be on would freeze it for every
		/// observer while it carried on fighting.
		/// </remarks>
		[TestCase(true, false, false, TestName = "Player, forwarding off -> transform stays on")]
		[TestCase(true, true, true, TestName = "Player, forwarding on -> transform is redundant")]
		[TestCase(false, false, false, TestName = "NPC, forwarding off -> transform stays on")]
		[TestCase(false, true, false, TestName = "NPC, forwarding on -> transform STILL stays on")]
		public void ModeDetection_TransportRuleNeedsBothInputs(bool hasKccPlayer, bool forwarding, bool expectRedundant)
		{
			LogAssert.AreEqual(expectRedundant,
				CharacterPredictionController.IsTransformRedundant(hasKccPlayer, forwarding),
				$"KCCPlayer={hasKccPlayer}, forwarding={forwarding}: the NetworkTransform is only " +
				"redundant when prediction genuinely moves this character AND the reconcile is " +
				"being forwarded to observers.");
		}

		/// <summary>
		/// The mode is applied at network initialisation, and the flag itself is read live on every
		/// send rather than cached.
		/// </summary>
		/// <remarks>
		/// Two halves. <c>ApplyObserverTransportMode</c> is state that has to be <i>set</i> — the
		/// NetworkTransform's synchronised properties — so it runs once from
		/// <c>OnStartNetwork</c>, which is where the inspector value is first available. Everything
		/// else asks <c>ObserverSyncMode</c> per send, so a runtime flip needs no re-initialisation
		/// at those sites.
		/// </remarks>
		[Test]
		public void ModeDetection_AppliedAtInitialisation_AndTheFlagIsReadLive()
		{
			string path = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterPredictionController.cs");
			string source = File.ReadAllText(path);

			int start = source.IndexOf("public override void OnStartNetwork()", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "CharacterPredictionController must override OnStartNetwork.");
			string body = ExtractMethodBody(source, start);
			LogAssert.IsTrue(body.Contains("ApplyObserverTransportMode()"),
				"The transport mode must be applied from OnStartNetwork, so a prefab authored with " +
				"state forwarding on or off gets the matching transport without any further call.");

			// ObserverSyncMode must never cache: the property is re-read per send.
			string modePath = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/ObserverSyncMode.cs");
			string modeSource = File.ReadAllText(modePath);
			LogAssert.IsFalse(modeSource.Contains("static bool cached") || modeSource.Contains("private static bool "),
				"ObserverSyncMode must hold no cached state; every caller reads EnableStateForwarding live.");
		}

		// ── DETERMINISM: TICK ALIGNMENT ──────────────────────────────────────

		/// <summary>
		/// Nothing in the prediction directory reads a wall clock or a frame delta.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The simulation must advance on <c>TimeManager</c> ticks alone: every peer runs the same
		/// tick with the same fixed <c>TickDelta</c>, so a frame-rate-dependent value would make the
		/// same tick produce different results on the server and the owner and diverge them.
		/// </para>
		/// <para>
		/// Two exemptions, both outside the simulation and both asserted by name so a third cannot
		/// be added silently. <c>BuffController.ObservedBuffsReceivedTime</c> is wall-clock on
		/// purpose: an observed buff's remaining duration travels in SECONDS precisely because the
		/// receiving client's tick domain is its own, and the value is only ever used to age a
		/// display number. <c>KCCController</c>'s two accumulators are fed the fixed
		/// <c>TimeManager.TickDelta</c> from <c>KCCPlayer.OnReplicate</c> and are themselves
		/// reconciled fields of <c>KinematicCharacterMotorState</c>.
		/// </para>
		/// </remarks>
		[Test]
		public void Determinism_NothingInThePredictionPathReadsAWallClock()
		{
			string root = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/Prediction");
			LogAssert.IsTrue(Directory.Exists(root), $"Prediction directory not found at {root}.");

			string[] banned =
			{
				"Time.deltaTime", "Time.fixedDeltaTime", "Time.time",
				"Time.realtimeSinceStartup", "Time.smoothDeltaTime",
			};
			// file → the single token it is allowed to use, with the reason in the remarks above.
			Dictionary<string, string> exempt = new Dictionary<string, string>
			{
				{ "BuffController.cs", "Time.unscaledTime" },
			};

			List<string> offenders = new List<string>();
			foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(path);
				foreach (string line in File.ReadAllLines(path))
				{
					string trimmed = line.TrimStart();
					// Documentation that names a banned API to explain why it is not used.
					if (trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
					{
						continue;
					}
					foreach (string token in banned)
					{
						if (line.Contains(token))
						{
							offenders.Add($"{name}: {token} in `{line.Trim()}`");
						}
					}
					if (line.Contains("Time.unscaledTime") &&
						(!exempt.TryGetValue(name, out string allowed) || allowed != "Time.unscaledTime"))
					{
						offenders.Add($"{name}: Time.unscaledTime in `{line.Trim()}`");
					}
				}
			}

			TestContext.WriteLine($"MEASURE wall-clock reads in the prediction path: {offenders.Count}");
			LogAssert.AreEqual(0, offenders.Count,
				"The prediction path must advance on TimeManager ticks only. Offenders:\n  " +
				string.Join("\n  ", offenders));
		}

		/// <summary>
		/// The per-observer LOD scheduler runs on the tick clock, not the frame clock.
		/// </summary>
		/// <remarks>
		/// It decides how often each observer hears from a transform, and the sends themselves are
		/// gated on <c>tick % interval</c> — so scheduling it from <c>Update()</c> put the scheduler
		/// and the thing it schedules on two different clocks, and on a headless server the frame
		/// rate is unrelated to the tick rate.
		/// </remarks>
		[Test]
		public void Determinism_DistanceLodSchedulerIsTickDriven()
		{
			string path = Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/NetworkTransformDistanceLod.cs");
			string source = File.ReadAllText(path);

			LogAssert.IsFalse(source.Contains("private void Update()"),
				"The LOD scheduler must not run from Update(); a headless server's frame rate is " +
				"unrelated to its tick rate.");
			LogAssert.IsTrue(source.Contains("TimeManager.OnPostTick += TimeManager_OnPostTick"),
				"The LOD scheduler must subscribe to TimeManager.OnPostTick.");
			LogAssert.IsTrue(source.Contains("TimeManager.OnPostTick -= TimeManager_OnPostTick"),
				"The LOD scheduler must unsubscribe on stop, or a pooled object leaks a subscription.");
			LogAssert.IsTrue(source.Contains("evaluateIntervalTicks"),
				"The evaluation cadence must be expressed in ticks.");
		}

		// ── helpers ──────────────────────────────────────────────────────────

		/// <summary>Returns the braced body of the method whose signature starts at <paramref name="start"/>.</summary>
		private static string ExtractMethodBody(string source, int start)
		{
			int open = source.IndexOf('{', start);
			if (open < 0)
			{
				return string.Empty;
			}
			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{') depth++;
				else if (source[i] == '}')
				{
					depth--;
					if (depth == 0)
					{
						return source.Substring(open, i - open + 1);
					}
				}
			}
			return source.Substring(open);
		}

		private static CharacterAttributesBroadcast RoundTripAttributes(CharacterAttributesBroadcast m)
		{
			Writer w = new Writer();
			w.WriteCharacterAttributesBroadcast(m);
			return new Reader(w.GetArraySegment(), null).ReadCharacterAttributesBroadcast();
		}

		/// <summary>
		/// Round-trips the resource broadcast through the shape FishNet's codegen emits for it.
		/// </summary>
		private static CharacterResourcesBroadcast RoundTripResources(CharacterResourcesBroadcast m)
		{
			Writer w = new Writer();
			w.WriteInt32(m.CharacterObjectID);
			w.WriteInt32(m.Health); w.WriteInt32(m.MaxHealth);
			w.WriteInt32(m.Mana); w.WriteInt32(m.MaxMana);
			w.WriteInt32(m.Stamina); w.WriteInt32(m.MaxStamina);
			Reader r = new Reader(w.GetArraySegment(), null);
			return new CharacterResourcesBroadcast
			{
				CharacterObjectID = r.ReadInt32(),
				Health = r.ReadInt32(), MaxHealth = r.ReadInt32(),
				Mana = r.ReadInt32(), MaxMana = r.ReadInt32(),
				Stamina = r.ReadInt32(), MaxStamina = r.ReadInt32(),
			};
		}

		private static EquipmentObservedSlotBroadcast RoundTripEquipmentSlot(EquipmentObservedSlotBroadcast m)
		{
			Writer w = new Writer();
			w.WriteInt32(m.CharacterObjectID);
			w.WriteUInt8Unpacked(m.Slot);
			w.WriteInt32(m.TemplateID);
			w.WriteInt32(m.Seed);
			Reader r = new Reader(w.GetArraySegment(), null);
			return new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = r.ReadInt32(),
				Slot = r.ReadUInt8Unpacked(),
				TemplateID = r.ReadInt32(),
				Seed = r.ReadInt32(),
			};
		}

		private CharacterAttributeController MakeAttributeController(string name, out int[] attributeIDs)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);

			CharacterAttributeController controller = go.AddComponent<CharacterAttributeController>();
			controller.InitializeOnce(new MockCharacter(9));

			attributeIDs = new int[4];
			for (int i = 0; i < attributeIDs.Length; ++i)
			{
				CharacterAttributeTemplate t = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
				t.name = $"{name}_Attr_{i}";
				t.InitialValue = 10 + i;
				t.AddToCache(t.name);
				assets.Add(t);
				attributeIDs[i] = t.ID;
				controller.AddAttribute(new CharacterAttribute(controller, t.ID, t.InitialValue, 0));
			}

			AddResource(controller, name, "Health", HealthTemplateID);
			AddResource(controller, name, "Mana", ManaTemplateID);
			AddResource(controller, name, "Stamina", StaminaTemplateID);
			return controller;
		}

		private void AddResource(CharacterAttributeController controller, string owner, string label, int _)
		{
			CharacterAttributeTemplate t = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			t.name = $"{owner}_{label}";
			t.InitialValue = 1000;
			t.IsResourceAttribute = true;
			t.AddToCache(t.name);
			assets.Add(t);
			controller.AddResourceAttribute(new CharacterResourceAttribute(controller, t.ID, 1000, 1000, 0));

			switch (label)
			{
				case "Health": controller.HealthResourceTemplateID = t.ID; break;
				case "Mana": controller.ManaResourceTemplateID = t.ID; break;
				case "Stamina": controller.StaminaResourceTemplateID = t.ID; break;
			}
		}

		private BuffController MakeBuffController(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			BuffController controller = go.AddComponent<BuffController>();
			SetPrivate(controller, "tickDelta", 1f / 30f);
			SetPrivate(controller, "lastReplicateTick", 100u);
			SetPrivate(controller, "hasSeenFirstReplicate", true);
			controller.InitializeOnce(new MockCharacter(9));
			return controller;
		}

		private EquipmentController MakeEquipmentController(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			EquipmentController controller = go.AddComponent<EquipmentController>();
			controller.OnAwake();
			controller.InitializeOnce(new MockCharacter(9));
			return controller;
		}

		/// <summary>
		/// Builds a cooldown controller whose NetworkObject exists but is not owned locally — the
		/// observer's position — with state forwarding set as requested.
		/// </summary>
		private CooldownController MakeGuardedCooldownController(string name, bool forwarding)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);

			NetworkObject nob = go.AddComponent<NetworkObject>();
			SetPrivate(nob, "_enablePrediction", true);
			SetPrivate(nob, "_enableStateForwarding", forwarding);

			CooldownController controller = go.AddComponent<CooldownController>();
			SetPrivate(controller, "_networkObjectCache", nob);
			/* OnStartNetwork never runs on an unspawned probe, and TickDelta throws rather than
			 * falling back to a wall clock — deliberately, so a cooldown can never be computed from
			 * a non-deterministic delta. Seed the cache the way OnStartNetwork would. */
			SetPrivate(controller, "cachedTickDelta", 1f / 30f);
			controller.InitializeOnce(new MockCharacter(9));

			LogAssert.AreEqual(forwarding, ObserverSyncMode.ObserversConsumeReconcile(nob),
				"The probe must actually be in the mode the test asked for.");
			LogAssert.IsFalse(nob.IsOwner, "The probe must not be locally owned; it stands in for an observer.");
			return controller;
		}

		private static void ApplyObservedBuffs(BuffController controller, ObservedBuffEntry[] entries)
		{
			MethodInfo m = typeof(BuffController).GetMethod("ApplyObservedBuffs",
				BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(m, "BuffController.ApplyObservedBuffs must exist — it is the receive path.");
			m.Invoke(controller, new object[] { entries });
		}

		private static void ApplyObservedSlot(EquipmentController controller, EquipmentObservedSlotBroadcast msg)
		{
			controller.ApplyObservedSlot(msg.Slot, msg.TemplateID, msg.Seed);
		}

		private BaseItemTemplate MakeItemTemplate(string name)
		{
			ProofItemTemplate t = ScriptableObject.CreateInstance<ProofItemTemplate>();
			t.name = name;
			t.AddToCache(t.name);
			assets.Add(t);
			return t;
		}

		private static T GetPrivate<T>(object o, string field)
		{
			System.Type type = o.GetType();
			FieldInfo f = null;
			while (type != null && f == null)
			{
				f = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				type = type.BaseType;
			}
			LogAssert.IsNotNull(f, $"Field '{field}' not found on {o.GetType().Name}.");
			return (T)f.GetValue(o);
		}

		private static void SetPrivate<T>(object o, string field, T value)
		{
			Type type = o.GetType();
			FieldInfo f = null;
			while (type != null && f == null)
			{
				f = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				type = type.BaseType;
			}
			LogAssert.IsNotNull(f, $"Field '{field}' not found on {o.GetType().Name}.");
			f.SetValue(o, value);
		}

		private sealed class ProofItemTemplate : BaseItemTemplate { }

		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour b) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour b) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
