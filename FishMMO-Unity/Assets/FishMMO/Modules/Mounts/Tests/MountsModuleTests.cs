using System.Collections.Generic;
using System.IO;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Shared.WorldDesign;
using FishNet.Managing.Timing;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Mounts module's seams and rules (issue #109): the saddle socket, the mount part, a mount that lasts until removed
	/// and is saved, the saddle height, the Stablemaster's place at a stable door.
	/// </summary>
	[TestFixture]
	public class MountsModuleTests
	{
		private readonly List<Object> made = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in made)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			made.Clear();
		}

		private T Make<T>() where T : ScriptableObject
		{
			T instance = ScriptableObject.CreateInstance<T>();
			made.Add(instance);
			return instance;
		}

		// ── The saddle socket ──────────────────────────────────────────────────────────────

		/// <summary>Appended with the next number: item templates serialize the slot as its integer (ItemSlot's remarks).</summary>
		[Test]
		public void MountSaddle_IsSlotTen_AndNamedForPlayers()
		{
			Assert.That((int)ItemSlot.MountSaddle, Is.EqualTo(10));
			Assert.That(ItemSlotNames.DisplayName(ItemSlot.MountSaddle), Is.EqualTo("Saddle"));
			Assert.That(System.Enum.GetValues(typeof(ItemSlot)).Length, Is.EqualTo(11));
		}

		/// <summary>The sheet draws every slot: the names array and the markup both carry the saddle socket.</summary>
		[Test]
		public void CharacterSheet_HasTheSaddleSocket()
		{
			Assert.That(CharacterSheetView.SlotElementNames.Length, Is.EqualTo(System.Enum.GetValues(typeof(ItemSlot)).Length));
			Assert.That(CharacterSheetView.SlotElementNames[(int)ItemSlot.MountSaddle], Is.EqualTo("slot-saddle"));
			string uxml = File.ReadAllText("Assets/FishMMO/Client/GUI/World/CharacterSheet/UICharacterSheet.uxml");
			Assert.That(uxml, Does.Contain("name=\"slot-saddle\""));
		}

		/// <summary>A saddle goes in the saddle socket whatever was authored.</summary>
		[Test]
		public void Saddle_AlwaysSitsInTheSaddleSocket()
		{
			SaddleTemplate saddle = Make<SaddleTemplate>();
			saddle.Slot = ItemSlot.Head;
			typeof(SaddleTemplate).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
				.Invoke(saddle, null);
			Assert.That(saddle.Slot, Is.EqualTo(ItemSlot.MountSaddle));
		}

		// ── The part and the hooks ─────────────────────────────────────────────────────────

		[Test]
		public void EveryPlayer_GetsTheMountPart()
		{
			Assert.That(CharacterParts.PartsFor(typeof(PlayerCharacter)), Has.Member(typeof(MountController)));
		}

		[Test]
		public void EnsureHooks_RegistersThePressOverride()
		{
			MountsModule.EnsureHooks();
			MountsModule.EnsureHooks();
			Assert.That(MountPressOverride.IsMountTemplate(null), Is.False);
		}

		// ── A mount lasts until removed, and is saved ──────────────────────────────────────

		[Test]
		public void MountBuff_NeverExpires_ButIsSavedLikeAnyBuff()
		{
			MountBuffTemplate mount = Make<MountBuffTemplate>();
			mount.Duration = 0f;
			Assert.That(mount.LastsUntilRemoved, Is.True);
			Assert.That(Buff.GetExpiryTick(mount, 1000u, 1f / 30f), Is.EqualTo(TimeManager.UNSET_TICK),
				"a zero duration would otherwise expire it on its first tick");
			Assert.That(BuffController.IsPersistedBuff(mount), Is.True, "a rider logs back in mounted");
			Assert.That(mount.IsPermanent, Is.False, "permanent buffs are the environment's: never saved, and a death keeps them");
		}

		[Test]
		public void MountBuff_ToldToTheMotorAsItsMotion()
		{
			MountBuffTemplate mount = Make<MountBuffTemplate>();
			mount.GroundSpeedMultiplier = 1.7f;
			mount.JumpHeightMultiplier = 2.5f;
			mount.LeapSpeed = 6f;
			mount.MaxSlopeDegrees = 75f;
			mount.Flies = true;
			mount.FlySpeed = 15f;
			MountMotion motion = mount.Motion;
			Assert.That(motion.GroundSpeedMultiplier, Is.EqualTo(1.7f));
			Assert.That(motion.JumpHeightMultiplier, Is.EqualTo(2.5f));
			Assert.That(motion.LeapSpeed, Is.EqualTo(6f));
			Assert.That(motion.MaxSlopeDegrees, Is.EqualTo(75f));
			Assert.That(motion.Flies, Is.True);
			Assert.That(motion.FlySpeed, Is.EqualTo(15f));
		}

		[Test]
		public void SaddleHeight_IsTheBackPlusTheSaddle()
		{
			var body = new WildlifeBodySpec { LegLength = 0.9f, Depth = 0.62f };
			Assert.That(MountRules.SaddleHeight(body), Is.EqualTo(0.9f + 0.62f + MountRules.SaddleThickness).Within(0.0001f));
		}

		[Test]
		public void Describe_NamesWhatTheMountChanges()
		{
			MountBuffTemplate dragon = Make<MountBuffTemplate>();
			dragon.GroundSpeedMultiplier = 1.4f;
			dragon.Flies = true;
			var lines = new List<(string, string)>(MountRules.Describe(dragon));
			Assert.That(lines.Exists(l => l.Item1 == "Speed" && l.Item2 == "+40%"));
			Assert.That(lines.Exists(l => l.Item1 == "Flies"));
		}

		[Test]
		public void NobodyRides_WithoutASaddle()
		{
			Assert.That(MountRules.RefusalFor(null), Is.Not.Null);
			Assert.That(MountRules.SaddleMount((IItemContainer)null), Is.Null);
			Assert.That(MountRules.ActiveMount((SortedDictionary<int, Buff>)null), Is.Null);
		}

		// ── The Stablemaster at the stable door ────────────────────────────────────────────

		[Test]
		public void SiteLocal_IsTheInverseOfTheSiteFrame()
		{
			Vector3 site = new Vector3(100f, 5f, -40f);
			const float yaw = 37f;
			Vector2 local = new Vector2(6f, -3f);
			Vector3 world = site + Quaternion.Euler(0f, yaw, 0f) * new Vector3(local.x, 0f, local.y);
			Vector2 back = StablemasterSiteBuilder.ToSiteLocal(site, yaw, world);
			Assert.That(Vector2.Distance(back, local), Is.LessThan(0.001f));
		}

		[Test]
		public void Stablemaster_StandsOutsideTheFirstStableDoor()
		{
			var doors = new List<PointOfInterestSiteContext.PlacedDoor>
			{
				new PointOfInterestSiteContext.PlacedDoor { Building = "Tavern", Hinge = Vector3.zero, Across = Vector3.right, Inward = Vector3.forward, Width = 1.2f },
				new PointOfInterestSiteContext.PlacedDoor { Building = "Stables", Hinge = new Vector3(10f, 0f, 0f), Across = Vector3.right, Inward = Vector3.forward, Width = 2f },
			};
			Assert.That(StablemasterSiteBuilder.TryFindStableDoor(doors, out PointOfInterestSiteContext.PlacedDoor door), Is.True);
			Assert.That(door.Building, Is.EqualTo("Stables"));
			Vector3 spot = StablemasterSiteBuilder.StandingSpot(door);
			Assert.That(spot.x, Is.EqualTo(11f).Within(0.001f), "the middle of the doorway");
			Assert.That(spot.z, Is.EqualTo(-StablemasterSiteBuilder.StandOff).Within(0.001f), "out on the street, not inside");
			Assert.That(StablemasterSiteBuilder.TryFindStableDoor(doors.GetRange(0, 1), out _), Is.False, "no stables, no Stablemaster");
		}
	}
}
