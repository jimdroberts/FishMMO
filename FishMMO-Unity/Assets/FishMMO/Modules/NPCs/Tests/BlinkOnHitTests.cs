using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.AI;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins <see cref="BlinkToHitTargetAction"/> (where around a struck target the caster lands) and the Blink On Hit content.
	/// </summary>
	[TestFixture]
	public class BlinkOnHitTests
	{
		private const string BlinkEvents = "Assets/FishMMO/Modules/Abilities/Templates/Events/Hit/Blink";
		private const string Shadowstep = "Assets/FishMMO/Modules/Abilities/Templates/Types/Blink/Shadowstep.asset";
		private const string BlinkBolt = "Assets/FishMMO/Modules/Abilities/Templates/Types/Blink/Blink Bolt.asset";
		private const string Merchant = "Assets/FishMMO/Modules/Commerce/Templates/NPCs/Human/Merchants/GeneralGoods/GeneralGoods.asset";

		// ── Where ────────────────────────────────────────────────────────────────────────────

		[Test]
		public void Placements_AreAroundTheFrame()
		{
			Vector3 north = Vector3.forward;
			Assert.That(BlinkToHitTargetAction.PlacementDirection(BlinkPlacement.InFront, north), Is.EqualTo(Vector3.forward));
			Assert.That(BlinkToHitTargetAction.PlacementDirection(BlinkPlacement.Behind, north), Is.EqualTo(Vector3.back));
			Assert.That(BlinkToHitTargetAction.PlacementDirection(BlinkPlacement.Right, north), Is.EqualTo(Vector3.right));
			Assert.That(BlinkToHitTargetAction.PlacementDirection(BlinkPlacement.Left, north), Is.EqualTo(Vector3.left));
			Assert.That(BlinkToHitTargetAction.PlacementDirection(BlinkPlacement.Above, north), Is.EqualTo(Vector3.zero));
		}

		[Test]
		public void TheFrame_IsTheTargetsFacingOrTheTargetTurnedToItsAttacker()
		{
			Vector3 facingEast = Vector3.right;
			Vector3 casterToTarget = new Vector3(0f, 2f, 5f); // the caster is below and south of the target
			Assert.That(BlinkToHitTargetAction.FrameForward(BlinkFrame.TargetFacing, facingEast, casterToTarget, Vector3.left), Is.EqualTo(Vector3.right));

			Vector3 turned = BlinkToHitTargetAction.FrameForward(BlinkFrame.AttackDirection, facingEast, casterToTarget, Vector3.left);
			Assert.That(turned, Is.EqualTo(Vector3.back), "flat, from the target toward the caster");
			Assert.That(BlinkToHitTargetAction.PlacementDirection(BlinkPlacement.Behind, turned), Is.EqualTo(Vector3.forward),
				"Behind is the far side of the target from the caster");
			Assert.That(BlinkToHitTargetAction.PlacementDirection(BlinkPlacement.InFront, turned), Is.EqualTo(Vector3.back),
				"In Front is between the two");

			Assert.That(BlinkToHitTargetAction.FrameForward(BlinkFrame.AttackDirection, facingEast, Vector3.up, Vector3.left), Is.EqualTo(Vector3.right),
				"no flat direction between them: the caster's forward, reversed to face it");
		}

		[Test]
		public void Landing_IsBehindInTheOpen_GivesWayToAnotherSideAtAWall_AndGoesOverheadWithHeadroom()
		{
			Vector3 target = new Vector3(-7000f, 40f, 7000f);
			GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
			GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
			try
			{
				floor.transform.position = target + new Vector3(0f, -0.5f, 0f);
				floor.transform.localScale = new Vector3(40f, 1f, 40f);
				wall.transform.position = target + new Vector3(0f, -100f, 0f); // out of the way to start
				Physics.SyncTransforms();
				PhysicsScene physics = floor.scene.GetPhysicsScene();

				Vector3 behind = BlinkToHitTargetAction.ResolveDestination(physics, target, 1.8f, Vector3.forward, BlinkPlacement.Behind, 1.2f, 0.3f, 1.8f, ~0);
				Assert.That(Vector3.Distance(behind, target + Vector3.back * 1.2f), Is.LessThan(0.01f), "1.2 m at its back");

				// A wall right at its back (0.4 m): no room, so the next side (Left) is taken.
				wall.transform.position = target + new Vector3(0f, 1.5f, -0.65f);
				wall.transform.localScale = new Vector3(6f, 3f, 0.5f);
				Physics.SyncTransforms();
				Vector3 fallback = BlinkToHitTargetAction.ResolveDestination(physics, target, 1.8f, Vector3.forward, BlinkPlacement.Behind, 1.2f, 0.3f, 1.8f, ~0);
				Assert.That(Vector3.Distance(fallback, target + Vector3.left * 1.2f), Is.LessThan(0.01f), "the wall's side gave way to the left");

				// Above: open sky, so over its head.
				Vector3 above = BlinkToHitTargetAction.ResolveDestination(physics, target, 1.8f, Vector3.forward, BlinkPlacement.Above, 0.4f, 0.3f, 1.8f, ~0);
				Assert.That(above.y - target.y, Is.EqualTo(1.8f + 0.1f).Within(0.01f));
				Assert.That(new Vector2(above.x - target.x, above.z - target.z).magnitude, Is.LessThan(0.01f));

				// A low ceiling: no headroom, so a step behind (or the side that has room) instead.
				wall.transform.position = target + new Vector3(0f, 2.6f, 0f);
				wall.transform.localScale = new Vector3(1.0f, 0.3f, 1.0f);
				Physics.SyncTransforms();
				Vector3 lowCeiling = BlinkToHitTargetAction.ResolveDestination(physics, target, 1.8f, Vector3.forward, BlinkPlacement.Above, 0.4f, 0.3f, 1.8f, ~0);
				Assert.That(lowCeiling.y, Is.EqualTo(target.y).Within(0.01f), "on the ground beside it");
				Assert.That(new Vector2(lowCeiling.x - target.x, lowCeiling.z - target.z).magnitude, Is.GreaterThan(0.6f));
			}
			finally
			{
				Object.DestroyImmediate(floor);
				Object.DestroyImmediate(wall);
			}
		}

		[Test]
		public void NpcsBlinkThroughTheirBrain()
		{
			Assert.That(typeof(INPCBrain).GetMethod(nameof(INPCBrain.BlinkTo)), Is.Not.Null);
			Assert.That(typeof(INPCBrain).IsAssignableFrom(typeof(AIController)), Is.True);
		}

		// ── The content ──────────────────────────────────────────────────────────────────────

		[Test]
		public void FiveComponents_OnePerPlacement_AllSold()
		{
			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(ProjectFiles.Path(Merchant));
			foreach ((BlinkPlacement placement, string label) in new[]
			{
				(BlinkPlacement.InFront, "In Front"), (BlinkPlacement.Behind, "Behind"), (BlinkPlacement.Left, "Left"),
				(BlinkPlacement.Right, "Right"), (BlinkPlacement.Above, "Above"),
			})
			{
				string path = $"{BlinkEvents}/Blink On Hit - {label}.asset";
				AbilityOnHitEvent component = AssetDatabase.LoadAssetAtPath<AbilityOnHitEvent>(ProjectFiles.Path(path));
				if (component == null) Assert.Inconclusive($"'{path}' has not been generated.");
				BlinkToHitTargetAction blink = component.OnConditionsMetActions.OfType<BlinkToHitTargetAction>().Single();
				Assert.That(blink.Placement, Is.EqualTo(placement), label);
				Assert.That(blink.DepartFX != null && blink.ArriveFX != null, Is.True, $"{label}: an effect at each end");
				Assert.That(component.TargetSelector, Is.InstanceOf<EventTargetSelector>(), $"{label}: the character the hit struck");
				Assert.That(merchant.AbilityEvents, Does.Contain(component), $"{label} is sold");
				Assert.That(AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path)), Is.Not.Null, $"{label} is addressable");
			}
		}

		[Test]
		public void Shadowstep_StrikesAndLandsBehind_BlinkBolt_HasASlotToCraftOneIn()
		{
			AbilityTemplate shadowstep = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(ProjectFiles.Path(Shadowstep));
			AbilityTemplate bolt = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(ProjectFiles.Path(BlinkBolt));
			if (shadowstep == null || bolt == null) Assert.Inconclusive("not generated");

			BlinkToHitTargetAction blink = shadowstep.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<BlinkToHitTargetAction>().Single();
			Assert.That(blink.Placement, Is.EqualTo(BlinkPlacement.Behind));
			Assert.That(shadowstep.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<ApplyDamageAction>().Any(), Is.True);

			Assert.That(bolt.AdditionalEventSlots, Is.EqualTo(1));
			Assert.That(bolt.OnHitEvents.SelectMany(e => e.OnConditionsMetActions).OfType<BlinkToHitTargetAction>().Any(), Is.False, "the blink is the player's to add");

			MerchantTemplate merchant = AssetDatabase.LoadAssetAtPath<MerchantTemplate>(ProjectFiles.Path(Merchant));
			Assert.That(merchant.Abilities, Does.Contain(shadowstep));
			Assert.That(merchant.Abilities, Does.Contain(bolt));
			Assert.That(merchant.PremadeAbilities.Any(p => p != null && p.Ability == shadowstep), Is.True);
		}
	}
}
