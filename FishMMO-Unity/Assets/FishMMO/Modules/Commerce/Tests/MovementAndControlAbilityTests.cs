using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using FishMMO.Shared;
using FishNet.Serializing;
using KinematicCharacterController;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins Short Charge (staged charge, predicted rush), Lesser Teleport (blink) and Root (IsRooted), and the content the
	/// test ability suite generates for them (TestAbilitySuiteGenerator).
	/// </summary>
	[TestFixture]
	public class MovementAndControlAbilityTests
	{
		private const string ShortCharge = "Assets/FishMMO/Modules/Abilities/Templates/Types/Movement/Short Charge.asset";
		private const string LesserTeleport = "Assets/FishMMO/Modules/Abilities/Templates/Types/Movement/Lesser Teleport.asset";
		private const string Root = "Assets/FishMMO/Modules/Abilities/Templates/Types/Control/Root.asset";
		private const string RootBuff = "Assets/FishMMO/Modules/Buffs/Templates/Root.asset";
		private const string Merchant = "Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";
		private const string TestHuman = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.prefab";

		private static AbilityTemplate Load(string path)
		{
			AbilityTemplate t = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(ProjectFiles.Path(path));
			if (t == null)
			{
				Assert.Inconclusive($"'{path}' has not been generated (TestAbilitySuiteGenerator.GenerateAll).");
			}
			return t;
		}

		// ── The staged charge ────────────────────────────────────────────────────────────────

		[Test]
		public void ChargeStage_TapHalfAndFull()
		{
			const uint total = 30; // a 1 s charge at 30 Hz
			Assert.That(AbilityController.ResolveChargeStage(30, total, 3), Is.EqualTo(1), "released on the first tick: a tap");
			Assert.That(AbilityController.ResolveChargeStage(16, total, 3), Is.EqualTo(1), "just short of half");
			Assert.That(AbilityController.ResolveChargeStage(15, total, 3), Is.EqualTo(2), "half");
			Assert.That(AbilityController.ResolveChargeStage(1, total, 3), Is.EqualTo(2), "a tick short of full is still medium");
			Assert.That(AbilityController.ResolveChargeStage(0, total, 3), Is.EqualTo(3), "the full charge");
			Assert.That(AbilityController.ResolveChargeStage(40, total, 3), Is.EqualTo(1), "more left than the total: nothing elapsed");
			Assert.That(AbilityController.ResolveChargeStage(5, total, 1), Is.EqualTo(1), "not staged");
			Assert.That(AbilityController.ResolveChargeStage(5, 0, 3), Is.EqualTo(3), "an instant charge is full");
		}

		[Test]
		public void ChargeStageValue_PicksByStage_AndReadsStageOneWithoutOne()
		{
			ChargeStageValue value = new ChargeStageValue { PerStage = new[] { 4, 7, 10 } };
			Assert.That(value.GetValue(null, null), Is.EqualTo(4), "no stage on the event: stage 1");
			Assert.That(ChargeStageValue.IndexOf(0, 3), Is.EqualTo(0));
			Assert.That(ChargeStageValue.IndexOf(2, 3), Is.EqualTo(1));
			Assert.That(ChargeStageValue.IndexOf(9, 3), Is.EqualTo(2), "past the end: the last");
			Assert.That(value.Describe(), Is.EqualTo("4 / 7 / 10 by charge"));
		}

		// ── The rush as motor state ──────────────────────────────────────────────────────────

		[Test]
		public void RushHeadingAndSpeed_QuantiseAndComeBack()
		{
			foreach (float degrees in new[] { 0f, 37.5f, 90f, 181f, 359.9f })
			{
				Vector3 direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
				Vector3 back = KCCController.MotionDirectionOf(KCCController.QuantizeMotionYaw(direction));
				Assert.That(Vector3.Angle(direction, back), Is.LessThan(0.01f), $"{degrees}°");
			}
			Assert.That(KCCController.QuantizeMotionSpeed(19.286f), Is.EqualTo(1929));
			Assert.That(KCCController.QuantizeMotionSpeed(0f), Is.EqualTo(1), "never zero: a rush always moves");
			Assert.That(KCCController.QuantizeMotionSpeed(1e6f), Is.EqualTo(ushort.MaxValue));
		}

		[Test]
		public void TheRushRidesTheReconcileFullAndDelta()
		{
			foreach (Type t in new[] { typeof(CharacterTransientGroundingReportDeltaSerializer), typeof(KinematicCharacterMotorStateDeltaSerializer) })
			{
				t.GetMethod("RegisterSerializers", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, null);
			}
			KinematicCharacterMotorState rushing = default;
			rushing.Rotation = Quaternion.identity;
			rushing.Position = new Vector3(3f, 1f, 4f);
			rushing.MotionTicks = 14;
			rushing.MotionSpeed = 1929;
			rushing.MotionYaw = 40000;

			Writer full = new Writer();
			full.Write(rushing);
			KinematicCharacterMotorState fullBack = new Reader(full.GetArraySegment(), null).Read<KinematicCharacterMotorState>();
			Assert.That(fullBack.MotionTicks == 14 && fullBack.MotionSpeed == 1929 && fullBack.MotionYaw == 40000, Is.True, "full");

			KinematicCharacterMotorState still = rushing;
			still.MotionTicks = 0;
			still.MotionSpeed = 0;
			still.MotionYaw = 0;
			Writer delta = new Writer();
			delta.WriteDelta(still, rushing, DeltaSerializerOption.RootSerialize);
			KinematicCharacterMotorState deltaBack = new Reader(delta.GetArraySegment(), null).ReadDelta(still);
			Assert.That(deltaBack.MotionTicks == 14 && deltaBack.MotionSpeed == 1929 && deltaBack.MotionYaw == 40000, Is.True, "delta");

			// Counting down changes one byte: the mask, the extension's flags and that byte.
			KinematicCharacterMotorState next = rushing;
			next.MotionTicks = 13;
			Writer tick = new Writer();
			tick.WriteDelta(rushing, next, DeltaSerializerOption.RootSerialize);
			KinematicCharacterMotorState tickBack = new Reader(tick.GetArraySegment(), null).ReadDelta(rushing);
			Assert.That(tickBack.MotionTicks == 13 && tickBack.MotionSpeed == 1929 && tickBack.MotionYaw == 40000, Is.True);
			Assert.That(tick.Length, Is.EqualTo(2 + 1 + 1));
		}

		// ── Root ─────────────────────────────────────────────────────────────────────────────

		[Test]
		public void RootedIsAppended_AndIsNotIncapacitation()
		{
			Assert.That((int)CharacterFlags.IsRooted, Is.EqualTo((int)CharacterFlags.IsCombatLogged + 1), "appended: the flags are a persisted bit index");
			Assert.That(Enum.GetValues(typeof(CharacterFlags)).Cast<int>().Max(), Is.LessThan(32), "fits the int bitfield");
		}

		[Test]
		public void RootInput_HoldsTheFeet_NotTheCrouchOrTheFacing()
		{
			float forward = 1f, right = -0.5f;
			int flags = 0;
			flags.EnableBit(KCCMoveFlags.IsActualData);
			flags.EnableBit(KCCMoveFlags.Jump);
			flags.EnableBit(KCCMoveFlags.Sprint);
			flags.EnableBit(KCCMoveFlags.Ascend);
			flags.EnableBit(KCCMoveFlags.Crouch);
			CharacterIncapacitation.RootInput(ref forward, ref right, ref flags);
			Assert.That(forward, Is.Zero);
			Assert.That(right, Is.Zero);
			Assert.That(flags.IsFlagged(KCCMoveFlags.Jump) || flags.IsFlagged(KCCMoveFlags.Sprint) || flags.IsFlagged(KCCMoveFlags.Ascend), Is.False);
			Assert.That(flags.IsFlagged(KCCMoveFlags.Crouch) && flags.IsFlagged(KCCMoveFlags.IsActualData), Is.True, "crouching in place is not moving");
		}

		// ── The blink's destination ──────────────────────────────────────────────────────────

		[Test]
		public void Blink_StopsShortOfAWall_GoesItsLengthInTheOpen_AndLandsOnTheGround()
		{
			Vector3 origin = new Vector3(7000f, 50f, 7000f);
			GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
			GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
			try
			{
				floor.transform.position = origin + new Vector3(0f, -0.5f, 0f);
				floor.transform.localScale = new Vector3(40f, 1f, 40f);
				wall.transform.position = origin + new Vector3(0f, 1.5f, 3f);
				wall.transform.localScale = new Vector3(4f, 3f, 0.5f);
				Physics.SyncTransforms();
				PhysicsScene physics = floor.scene.GetPhysicsScene();

				Vector3 blocked = TeleportCasterAction.ResolveDestination(physics, origin, Vector3.forward, 5f, 0.3f, 1.8f, ~0);
				float wallFace = 3f - 0.25f;
				Assert.That(blocked.z - origin.z, Is.InRange(wallFace - 0.3f - 0.15f, wallFace - 0.3f + 0.01f), "stops a body's radius short of the wall");
				Assert.That(blocked.y, Is.EqualTo(origin.y).Within(0.01f), "on the floor");

				Vector3 open = TeleportCasterAction.ResolveDestination(physics, origin, Vector3.left, 5f, 0.3f, 1.8f, ~0);
				Assert.That(Vector3.Distance(open, origin + Vector3.left * 5f), Is.LessThan(0.01f), "the full 5 m");

				// A kerb (lower than a step) does not stop it; the landing climbs onto it.
				wall.transform.position = origin + new Vector3(0f, 0.1f, 2.5f);
				wall.transform.localScale = new Vector3(4f, 0.2f, 6f);
				Physics.SyncTransforms();
				Vector3 kerb = TeleportCasterAction.ResolveDestination(physics, origin, Vector3.forward, 5f, 0.3f, 1.8f, ~0);
				Assert.That(kerb.z - origin.z, Is.EqualTo(5f).Within(0.01f));
				Assert.That(kerb.y - origin.y, Is.EqualTo(0.2f).Within(0.01f));
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(floor);
				UnityEngine.Object.DestroyImmediate(wall);
			}
		}

		// ── The generated content ────────────────────────────────────────────────────────────

		[Test]
		public void ShortCharge_IsAThreeStageHeldRushThatStrikes()
		{
			AbilityTemplate charge = Load(ShortCharge);
			Assert.That(charge.ChargeStages, Is.EqualTo(3));
			Assert.That(charge.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.Forward));
			Assert.That(charge.Speed, Is.Zero, "carried by FollowCasterAction, not a path of its own");

			CasterRushAction rush = charge.OnSpawnEvents.SelectMany(e => e.OnConditionsMetActions).OfType<CasterRushAction>().Single();
			CollectionAssert.AreEqual(new[] { 3f, 6f, 9f }, rush.StageDistances);
			float[] speeds = rush.StageDistances.Zip(rush.StageSeconds, (d, s) => d / s).ToArray();
			Assert.That(speeds[0] < speeds[1] && speeds[1] < speeds[2], Is.True, "quick, fast, sprint");
			Assert.That(charge.LifeTime, Is.GreaterThanOrEqualTo(rush.StageSeconds.Max()), "the striking edge outlasts the longest rush");

			Assert.That(charge.OnTickEvents.SelectMany(e => e.OnConditionsMetActions).OfType<FollowCasterAction>().Any(), Is.True);
			ApplyDamageAction damage = charge.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<ApplyDamageAction>().Single();
			CollectionAssert.AreEqual(new[] { 4, 7, 10 }, ((ChargeStageValue)damage.DamageValue).PerStage, "damage scales with the stage");
			Assert.That(charge.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<EndCasterRushAction>().Any(), Is.True, "it stops at what it hits");
		}

		[Test]
		public void LesserTeleport_BlinksFiveMetres_WithAnEffectAtEachEnd()
		{
			AbilityTemplate teleport = Load(LesserTeleport);
			Assert.That(teleport.AbilitySpawnTarget, Is.EqualTo(AbilitySpawnTarget.PointBlank), "an object, so observers see both ends");
			var actions = teleport.OnSpawnEvents.Single().OnConditionsMetActions;
			PlayFXAction depart = actions.OfType<PlayFXAction>().Single();
			TeleportCasterAction blink = actions.OfType<TeleportCasterAction>().Single();
			Assert.That(actions.IndexOf(depart), Is.LessThan(actions.IndexOf(blink)), "the departure is placed before the caster leaves");
			Assert.That(depart.FXPrefab, Is.Not.Null);
			Assert.That(blink.ArriveFX, Is.Not.Null);
			Assert.That(blink.MaxDistance, Is.EqualTo(5f));
		}

		[Test]
		public void Root_IsABoltThatRootsForFourSeconds_WithVinesAtTheAnkles()
		{
			AbilityTemplate root = Load(Root);
			Assert.That(root.Speed, Is.GreaterThan(0f));
			ApplyBuffAction apply = root.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<ApplyBuffAction>().Single();
			StateBuffTemplate buff = apply.BuffTemplate as StateBuffTemplate;
			Assert.That(buff, Is.Not.Null);
			Assert.That(AssetDatabase.GetAssetPath(buff), Is.EqualTo(RootBuff));
			Assert.That(buff.Flag, Is.EqualTo(CharacterFlags.IsRooted));
			Assert.That(buff.Duration, Is.EqualTo(4f));
			Assert.That(buff.IsDebuff, Is.True);

			GameObject vines = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(buff.FXPrefabReference.AssetGUID));
			FXBoneAttachment attachment = vines.GetComponent<FXBoneAttachment>();
			CollectionAssert.AreEquivalent(new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot }, attachment.Bones);
			Assert.That(attachment.KeepUpright, Is.True, "the ring stays level");
			Assert.That(attachment.Emitter.GetComponent<ParticleSystem>().main.loop, Is.True, "for as long as the root lasts");
			Assert.That(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(buff.FXPrefabReference.AssetGUID), Is.Not.Null);
			Assert.That(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(RootBuff)), Is.Not.Null,
				"the client resolves the buff by id");
		}

		[Test]
		public void TheRootVines_SitOnTheTestHumansAnkles()
		{
			BaseBuffTemplate buff = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(ProjectFiles.Path(RootBuff));
			GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectFiles.Path(TestHuman));
			if (buff == null || model == null) Assert.Inconclusive("not generated");
			GameObject fx = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(buff.FXPrefabReference.AssetGUID));

			GameObject meshRoot = new GameObject("MeshRoot");
			try
			{
				GameObject body = UnityEngine.Object.Instantiate(model, meshRoot.transform);
				GameObject worn = UnityEngine.Object.Instantiate(fx, meshRoot.transform);
				typeof(FXBoneAttachment).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(worn.GetComponent<FXBoneAttachment>(), null);
				Animator animator = body.GetComponentInChildren<Animator>();
				foreach (HumanBodyBones foot in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
				{
					Transform bone = HumanoidBones.Find(animator, animator.transform, foot);
					ParticleSystem[] seated = bone.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.transform.parent == bone).ToArray();
					Assert.That(seated.Length, Is.EqualTo(1), foot.ToString());
					Assert.That(Quaternion.Angle(seated[0].transform.rotation, Quaternion.identity), Is.LessThan(0.01f), "level");
				}
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(meshRoot);
			}
		}

		[Test]
		public void GeneralGoods_SellsTheThree()
		{
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(ProjectFiles.Path(Merchant));
			foreach (string path in new[] { ShortCharge, LesserTeleport, Root })
			{
				AbilityTemplate ability = Load(path);
				Assert.That(merchant.Abilities, Does.Contain(ability), ability.name);
				Assert.That(merchant.PremadeAbilities.Any(p => p != null && p.Ability == ability), Is.True, $"{ability.name} premade");
				foreach (AbilityEvent e in ability.OnHitEvents.Cast<AbilityEvent>().Concat(ability.OnSpawnEvents).Concat(ability.OnDestroyEvents).Concat(ability.OnTickEvents))
				{
					if (AssetDatabase.GetAssetPath(e).Contains("/Move/")) continue;
					Assert.That(merchant.AbilityEvents, Does.Contain(e), $"{ability.name}: {e.name}");
					Assert.That(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e))), Is.Not.Null, e.name);
				}
				Assert.That(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path)), Is.Not.Null, path);
			}
		}
	}
}
