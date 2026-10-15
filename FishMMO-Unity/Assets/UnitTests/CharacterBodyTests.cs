using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The character body every character wears (<see cref="CharacterBody"/>): how it splits a speed into gaits and the air
	/// into rising, top and fall; how a client reads a character it only watches (<see cref="ObservedMotion"/>); and that
	/// the TestHuman has replaced Ethan everywhere (source scans: run from the project root).
	/// </summary>
	[TestFixture]
	public class CharacterBodyTests
	{
		private const float WalkSpeed = 1.5f, RunSpeed = 4f;

		[Test]
		public void StandingStillIsIdle()
		{
			CharacterBody.Gait(0f, WalkSpeed, RunSpeed, out float idle, out float walk, out float run, out float rate);
			Assert.That(idle, Is.EqualTo(1f));
			Assert.That(walk + run, Is.EqualTo(0f));
			Assert.That(rate, Is.EqualTo(1f));
		}

		[Test]
		public void TheGaitsHandOverWithoutAJumpAndAlwaysSumToOne()
		{
			float lastWalkish = 0f;
			for (float speed = 0f; speed <= 9f; speed += 0.05f)
			{
				CharacterBody.Gait(speed, WalkSpeed, RunSpeed, out float idle, out float walk, out float run, out float rate);
				Assert.That(idle + walk + run, Is.EqualTo(1f).Within(1e-4f), $"at {speed:F2} m/s");
				float walkish = walk * 0.5f + run;
				Assert.That(walkish, Is.GreaterThanOrEqualTo(lastWalkish - 1e-4f), $"faster is never less of a run, at {speed:F2} m/s");
				Assert.That(walkish - lastWalkish, Is.LessThan(0.1f), $"no jump at {speed:F2} m/s");
				lastWalkish = walkish;
			}
		}

		[Test]
		public void PastTheRunClipsSpeedTheRunPlaysFasterButNotWithoutLimit()
		{
			CharacterBody.Gait(RunSpeed * 1.5f, WalkSpeed, RunSpeed, out _, out _, out float run, out float rate);
			Assert.That(run, Is.EqualTo(1f));
			Assert.That(rate, Is.EqualTo(1.5f).Within(1e-4f));
			CharacterBody.Gait(RunSpeed * 5f, WalkSpeed, RunSpeed, out _, out _, out _, out rate);
			Assert.That(rate, Is.EqualTo(2f), "a teleport-fast glide does not spin the legs");
		}

		[Test]
		public void InTheAirItRisesTopsOutAndFalls()
		{
			CharacterBody.AirShares(4f, out float up, out float top, out float down);
			Assert.That((up, top, down), Is.EqualTo((1f, 0f, 0f)), "rising fast");
			CharacterBody.AirShares(0f, out up, out top, out down);
			Assert.That((up, top, down), Is.EqualTo((0f, 1f, 0f)), "at the top");
			CharacterBody.AirShares(-5f, out up, out top, out down);
			Assert.That((up, top, down), Is.EqualTo((0f, 0f, 1f)), "coming down");
			for (float vy = -6f; vy <= 6f; vy += 0.25f)
			{
				CharacterBody.AirShares(vy, out up, out top, out down);
				Assert.That(up + top + down, Is.EqualTo(1f).Within(1e-4f), $"at {vy} m/s");
			}
		}

		// ── A watched character ──

		[Test]
		public void AWatchedCharacterSwimsAtTheMovementsOwnThresholds()
		{
			const float h = 1.6f, still = 10f;
			float enterFeet = still - Constants.Character.SwimEnterSubmersion * h;
			float exitFeet = still - Constants.Character.SwimExitSubmersion * h;
			Assert.That(ObservedMotion.Swimming(false, still, enterFeet + 0.01f, h), Is.False, "wading, just short of the enter mark");
			Assert.That(ObservedMotion.Swimming(false, still, enterFeet - 0.01f, h), Is.True, "past it");
			float between = (enterFeet + exitFeet) * 0.5f;
			Assert.That(ObservedMotion.Swimming(true, still, between, h), Is.True, "between the marks a swimmer keeps swimming");
			Assert.That(ObservedMotion.Swimming(false, still, between, h), Is.False, "and a wader keeps wading");
			Assert.That(ObservedMotion.Swimming(true, still, exitFeet + 0.01f, h), Is.False, "out past the exit mark");
			Assert.That(ObservedMotion.Swimming(true, float.NaN, 0f, h), Is.False, "no water");
		}

		[Test]
		public void AWatchedCharacterIsOnALadderOnlyWhereAClimberWouldHang()
		{
			var ladder = new SceneClimbable
			{
				Kind = SceneClimbableKind.Ladder,
				Bottom = new Vector3(0f, 0f, 0f),
				Top = new Vector3(0f, 4f, 0f),
				Out = new Vector3(0f, 0f, -1f),
				TopExit = new Vector3(0f, 0f, 1f),
				HalfWidth = 0.25f,
			};
			if (!ClimbingRules.IsValid(in ladder))
			{
				Assert.Inconclusive("this ladder is not valid under ClimbingRules; the fixture needs updating to its rules");
			}
			Assert.That(ObservedMotion.OnClimbable(in ladder, new Vector3(0f, 2f, -0.5f), 0.3f), Is.True, "hanging off its face");
			Assert.That(ObservedMotion.OnClimbable(in ladder, new Vector3(0f, 2f, -2.5f), 0.3f), Is.False, "two metres off it");
			Assert.That(ObservedMotion.OnClimbable(in ladder, new Vector3(0f, 6f, -0.5f), 0.3f), Is.False, "above its top");
			Assert.That(ObservedMotion.OnClimbable(in ladder, new Vector3(0f, -1f, -0.5f), 0.3f), Is.False, "below its foot");
		}

		// ── Ethan is gone (source scans) ──

		private const string ModelPath = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.fbx";
		private const string PrefabPath = "Assets/Prefabs/Client/Models/TestHuman/TestHuman.prefab";
		private const string EthanFolder = "Assets/Prefabs/Client/Models/EthanCharacter";
		private const string EthanPrefabGuid = "7737647c22c1fc64a88d5cd030c352ce";
		private const string EthanControllerGuid = "e2cf68ff4b1ffda45a77f7307dd789b9";
		private const string EthanModelGuid = "b235179bd2a63d1468dd430670338c55";

		private static string Guid(string path) => Regex.Match(File.ReadAllText(path + ".meta"), @"guid: (\w+)").Groups[1].Value;

		[Test]
		public void TheModelIsRiggedOnTheMasterSkeleton()
		{
			string text = Encoding.ASCII.GetString(File.ReadAllBytes(ModelPath));
			string[] required =
			{
				SkeletonBones.Root, SkeletonBones.Hips, SkeletonBones.Spine, SkeletonBones.Chest, SkeletonBones.Neck, SkeletonBones.Head,
				SkeletonBones.LeftShoulder, SkeletonBones.LeftUpperArm, SkeletonBones.LeftLowerArm, SkeletonBones.LeftHand,
				SkeletonBones.RightShoulder, SkeletonBones.RightUpperArm, SkeletonBones.RightLowerArm, SkeletonBones.RightHand,
				SkeletonBones.LeftUpperLeg, SkeletonBones.LeftLowerLeg, SkeletonBones.LeftFoot,
				SkeletonBones.RightUpperLeg, SkeletonBones.RightLowerLeg, SkeletonBones.RightFoot,
			};
			foreach (string bone in required)
			{
				// FBX node names are stored length-prefixed ("Model::<name>" in ASCII FBX, the bare name in binary).
				Assert.That(text.Contains(bone), Is.True, $"the model has no '{bone}' bone: armour and proportions bind by these names");
			}
			Assert.That(text.Contains("LeftArm\0") || text.Contains("LeftUpLeg"), Is.False, "the rig tool's own names are not shipped");
		}

		[Test]
		public void EveryRaceThatWoreEthanWearsTheTestHuman()
		{
			if (!File.Exists(PrefabPath + ".meta"))
			{
				Assert.Inconclusive("the TestHuman prefab has not been built yet (CharacterModelMigration.Run)");
			}
			string testHuman = Guid(PrefabPath);
			foreach (string race in new[] { "Human", "Elf", "Orc" })
			{
				string text = File.ReadAllText($"Assets/Templates/Entity/Races/Humanoid/{race}.asset");
				Match placeholder = Regex.Match(text, @"PlaceholderModel:\s*\n\s*m_AssetGUID: (\w+)");
				Assert.That(placeholder.Success, Is.True, race);
				Assert.That(placeholder.Groups[1].Value, Is.EqualTo(testHuman), $"{race} wears the TestHuman");
			}
		}

		[Test]
		public void NothingRefersToEthanAnyMore()
		{
			if (!File.Exists(PrefabPath + ".meta"))
			{
				Assert.Inconclusive("the TestHuman prefab has not been built yet (CharacterModelMigration.Run)");
			}
			Assert.That(Directory.Exists(EthanFolder), Is.False, "Ethan's folder is gone");
			string[] kinds = { ".prefab", ".unity", ".asset", ".mat", ".controller" };
			var users = Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories)
				.Where(path => kinds.Contains(Path.GetExtension(path)))
				.Where(path =>
				{
					string text = File.ReadAllText(path);
					return text.Contains(EthanPrefabGuid) || text.Contains(EthanControllerGuid) || text.Contains(EthanModelGuid);
				})
				.ToArray();
			Assert.That(users, Is.Empty, "assets still naming Ethan's prefab, model or controller");
		}

		[Test]
		public void EveryNpcThatHadEthanIsAnimated()
		{
			if (!File.Exists(PrefabPath + ".meta"))
			{
				Assert.Inconclusive("the TestHuman prefab has not been built yet (CharacterModelMigration.Run)");
			}
			string controller = Guid("Assets/Scripts/Shared/Implementation/Entity/Animation/CharacterAnimationController.cs");
			string testHuman = Guid(PrefabPath);
			string[] npcs =
			{
				"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc warrior.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Monsters/Orcs/an orc mage.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Pets/a lesser fire elemental.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Banker/HumanBanker.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/Merchants/GeneralGoods/HumanGeneralMerchant.prefab",
				"Assets/Prefabs/Shared/Entity/NPCs/Interactables/Human/AbilityCrafter/HumanAbilityCrafter.prefab",
			};
			foreach (string npc in npcs)
			{
				string text = File.ReadAllText(npc);
				Assert.That(text.Contains(controller), Is.True, $"{Path.GetFileNameWithoutExtension(npc)} has an animation controller");
				Assert.That(text.Contains(testHuman), Is.True, $"{Path.GetFileNameWithoutExtension(npc)} wears the TestHuman");
			}
		}

		[Test]
		public void TheCharacterPrefabCarriesItsClips()
		{
			if (!File.Exists(PrefabPath + ".meta"))
			{
				Assert.Inconclusive("the TestHuman prefab has not been built yet (CharacterModelMigration.Run)");
			}
			string prefab = File.ReadAllText(PrefabPath);
			Assert.That(prefab.Contains(Guid("Assets/Scripts/Shared/Implementation/Entity/Animation/CharacterModel.cs")), Is.True, "it is marked as a character model");
			string set = File.ReadAllText("Assets/Prefabs/Client/Models/TestHuman/TestHuman Animations.asset");
			string fbx = Guid(ModelPath);
			int fromModel = Regex.Matches(set, fbx).Count;
			Assert.That(fromModel, Is.GreaterThanOrEqualTo(12), "the strokes, tread, climb, punches, cast, guard, roll, death and sits come from the model");
			// The clip slots, not the m_ header every asset carries (its prefab links are {fileID: 0} by nature).
			string[] empty = Regex.Matches(set, @"^  ([A-Z]\w*): \{fileID: 0\}", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
			Assert.That(empty, Is.Empty, "no clip slot is empty");
		}
	}
}
