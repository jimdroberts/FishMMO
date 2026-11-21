using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// The player-set order a pet attack command tries its target choices in.
	/// </summary>
	/// <remarks>
	/// The order is packed into one int that rides a broadcast and a settings file, so the
	/// encoding is pinned here exactly: what the default is, what decodes, what is refused, and
	/// how the panel's move-up gesture reorders it. The rest pins by source that the value flows
	/// through the controller, the add message, the panel and the server.
	/// </remarks>
	[TestFixture]
	public class PetAttackPriorityTests
	{
		private static string Scripts =>
			Path.Combine(Directory.GetCurrentDirectory(), "Assets/FishMMO");

		[Test]
		public void DefaultIsPinnedThenCurrentThenHighestThreat()
		{
			PetAttackTarget[] order = new PetAttackTarget[PetAttackPriority.StepCount];
			LogAssert.IsTrue(PetAttackPriority.TryDecode(PetAttackPriority.Default, order), "the default must decode");
			LogAssert.IsTrue(order[0] == PetAttackTarget.Pinned && order[1] == PetAttackTarget.Current && order[2] == PetAttackTarget.HighestThreat,
				"the shipped order is pinned, then current, then highest threat");
		}

		[Test]
		public void EveryPermutationRoundTrips()
		{
			PetAttackTarget[] values = { PetAttackTarget.Pinned, PetAttackTarget.Current, PetAttackTarget.HighestThreat };
			PetAttackTarget[] order = new PetAttackTarget[PetAttackPriority.StepCount];
			int distinct = 0;
			foreach (PetAttackTarget a in values)
				foreach (PetAttackTarget b in values)
					foreach (PetAttackTarget c in values)
					{
						if (a == b || b == c || a == c) continue;
						int packed = PetAttackPriority.Encode(a, b, c);
						LogAssert.IsTrue(PetAttackPriority.IsValid(packed) && PetAttackPriority.TryDecode(packed, order) &&
							order[0] == a && order[1] == b && order[2] == c,
							$"{a},{b},{c} must survive a round trip");
						distinct++;
					}
			LogAssert.IsTrue(distinct == 6, "there are exactly six orders");
		}

		[Test]
		public void ZeroAndDuplicatesAreRefusedAndNormalizeToTheDefault()
		{
			LogAssert.IsTrue(!PetAttackPriority.IsValid(0), "0 — an unset field or an old client — is never a valid order");
			int duplicate = PetAttackPriority.Encode(PetAttackTarget.Pinned, PetAttackTarget.Pinned, PetAttackTarget.Current);
			LogAssert.IsTrue(!PetAttackPriority.IsValid(duplicate), "a step used twice is refused");
			LogAssert.IsTrue(PetAttackPriority.Normalize(0) == PetAttackPriority.Default &&
				PetAttackPriority.Normalize(duplicate) == PetAttackPriority.Default &&
				PetAttackPriority.Normalize(PetAttackPriority.Default) == PetAttackPriority.Default,
				"an invalid value normalizes to the default and a valid one is kept");
		}

		[Test]
		public void MoveUpSwapsWithThePreviousSlotAndTheTopStaysPut()
		{
			int start = PetAttackPriority.Default;
			int moved = PetAttackPriority.MoveUp(start, 2);
			PetAttackTarget[] order = new PetAttackTarget[PetAttackPriority.StepCount];
			PetAttackPriority.TryDecode(moved, order);
			LogAssert.IsTrue(order[0] == PetAttackTarget.Pinned && order[1] == PetAttackTarget.HighestThreat && order[2] == PetAttackTarget.Current,
				"moving the third step up puts it second");

			LogAssert.IsTrue(PetAttackPriority.MoveUp(start, 0) == start, "the first step has nowhere to go");
			LogAssert.IsTrue(PetAttackPriority.MoveUp(start, 7) == start, "an out-of-range slot changes nothing");
			LogAssert.IsTrue(PetAttackPriority.MoveUp(0, 1) == PetAttackPriority.MoveUp(PetAttackPriority.Default, 1),
				"moving within an invalid order starts from the default");
		}

		[Test]
		public void MoveDownSwapsWithTheNextSlotAndTheBottomStaysPut()
		{
			int start = PetAttackPriority.Default;
			PetAttackTarget[] order = new PetAttackTarget[PetAttackPriority.StepCount];

			PetAttackPriority.TryDecode(PetAttackPriority.MoveDown(start, 0), order);
			LogAssert.IsTrue(order[0] == PetAttackTarget.Current && order[1] == PetAttackTarget.Pinned && order[2] == PetAttackTarget.HighestThreat,
				"the first step can be moved: down one puts it second");

			LogAssert.IsTrue(PetAttackPriority.MoveDown(start, PetAttackPriority.StepCount - 1) == start, "the last step has nowhere to go");
			LogAssert.IsTrue(PetAttackPriority.MoveDown(start, -1) == start, "an out-of-range slot changes nothing");
			LogAssert.IsTrue(PetAttackPriority.MoveDown(0, 0) == PetAttackPriority.MoveDown(PetAttackPriority.Default, 0),
				"moving within an invalid order starts from the default");
			LogAssert.IsTrue(PetAttackPriority.MoveUp(PetAttackPriority.MoveDown(start, 1), 2) == start,
				"down then back up is where it started");
		}

		[Test]
		public void EveryPriorityRowHasAWorkingControl()
		{
			/* The first row used to carry only a move-up arrow, greyed out for good: a row the player
			 * could not move. Every row has both arrows; the direction a row cannot go is hidden. */
			string uxml = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Client/GUI/World/Pet/UIPetControl.uxml"));
			for (int i = 0; i < PetAttackPriority.StepCount; ++i)
			{
				LogAssert.IsTrue(uxml.Contains($"name=\"pet-priority-up-{i}\"") && uxml.Contains($"name=\"pet-priority-down-{i}\""),
					$"row {i} must have both arrows");
			}

			string panel = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Client/GUI/World/Pet/UITKPetControl.cs"));
			LogAssert.IsTrue(panel.Contains("priorityDownButtons[i].clicked += () => OnPriorityMoveDown(slot);") &&
				panel.Contains("attackPriority = PetAttackPriority.MoveDown(attackPriority, slot);") &&
				panel.Contains("SetMoveButton(priorityUpButtons[i], i > 0);") &&
				panel.Contains("SetMoveButton(priorityDownButtons[i], i < PetAttackPriority.StepCount - 1);"),
				"the down arrow must reorder, and each row must show only the arrows that can act");
		}

		[Test]
		public void ThePriorityFlowsFromPanelToServerAndBack()
		{
			string controller = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Shared/Implementation/Entity/NPC/Pet/PetController.cs"));
			LogAssert.IsTrue(controller.Contains("public int AttackPriority { get; set; } = PetAttackPriority.Default;") &&
				controller.Contains("RegisterBroadcast<PetAttackPriorityBroadcast>(OnClientPetAttackPriorityBroadcastReceived)") &&
				controller.Contains("AttackPriority = PetAttackPriority.Normalize(msg.AttackPriority);"),
				"the controller must hold the order, take the server's confirmation, and read it from the add message");

			string pets = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Server/Implementation/World/SceneServer/Pet/PetSystem.cs"));
			LogAssert.IsTrue(pets.Contains("RegisterBroadcast<PetAttackPriorityBroadcast>(OnPetAttackPriorityBroadcastReceived, true)") &&
				pets.Contains("if (PetAttackPriority.IsValid(msg.Priority))") &&
				pets.Contains("new PetAttackPriorityBroadcast() { Priority = petController.AttackPriority }") &&
				pets.Contains("pet.AttackPriority = petController.AttackPriority;") &&
				pets.Contains("AttackPriority = pet.AttackPriority,"),
				"the server must validate a request, confirm what it holds, apply the order at summon and send it with the add message");

			string panel = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Client/GUI/World/Pet/UITKPetControl.cs"));
			LogAssert.IsTrue(panel.Contains("priority.clicked += OnTogglePriorityPanel;") &&
				panel.Contains("priorityUpButtons[i].clicked += () => OnPriorityMoveUp(slot);") &&
				panel.Contains("attackPriority = PetAttackPriority.MoveUp(attackPriority, slot);") &&
				panel.Contains("ReplayStoredPriority(first.Pet);") &&
				panel.Contains("Configuration.GlobalSettings.Set(PRIORITY_SETTING_KEY, priority.ToString());"),
				"the panel must open the rows, reorder on the arrow, remember the order in the settings and replay it on summon");

			string uxml = ProjectFiles.ReadAllText(Path.Combine(Scripts, "Client/GUI/World/Pet/UIPetControl.uxml"));
			LogAssert.IsTrue(uxml.Contains("name=\"pet-priority\"") && uxml.Contains("name=\"pet-priority-panel\"") &&
				uxml.Contains("name=\"pet-priority-label-2\"") && uxml.Contains("name=\"pet-priority-up-2\""),
				"the panel's tree must carry the Priority button and three reorderable rows");
		}
	}
}
