using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The Achievements module's part of the panel render rig: a fake achievement controller and its fixture, and the
	/// achievements window in the all-panels render and the grid fit probe.
	/// </summary>
	public static class AchievementsRenderScratch
	{
		/// <summary>The achievements window's layout.</summary>
		public const string Uxml = "Assets/FishMMO/Modules/Achievements/Client/UIAchievements.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			Rig.ExtraFakes -= AddFakes;
			Rig.ExtraFakes += AddFakes;
			Fixtures.ExtraFixtures -= Achievements;
			Fixtures.ExtraFixtures += Achievements;
			AllPanelsRender.ExtraPopulators["UIAchievements"] = Window;
			if (!GridFitProbe.Windows.Exists(w => w.uxml == Uxml))
			{
				GridFitProbe.Windows.Add((Uxml, Window));
			}
		}

		/// <summary>Populates the achievements window for the character.</summary>
		public static void Window(GameObject h, UIDocument d) => Panels.Character<UITKAchievements>(h, d);

		private static void AddFakes(ICharacter character, Dictionary<Type, ICharacterBehaviour> behaviours)
		{
			Rig.Register<IAchievementController>(behaviours, new FakeAchievements(character));
		}

		private static void Achievements(ICharacter character)
		{
			if (!character.TryGet(out IAchievementController controller)) { return; }

			List<AchievementTemplate> templates =
				Fixtures.Load<AchievementTemplate>("Assets/FishMMO/Modules/Achievements/Templates");

			byte tier = 0;
			uint value = 5;
			foreach (AchievementTemplate t in templates)
			{
				controller.SetAchievement(t.ID, tier, value);
				tier = (byte)((tier + 1) % 3);
				value = value * 4 + 13;
			}
		}
	}

	/// <summary>Achievement controller fake — the Achievements panel reads <c>Achievements</c>.</summary>
	public sealed class FakeAchievements : IAchievementController
	{
		private readonly Dictionary<int, Achievement> achievements = new Dictionary<int, Achievement>();

		public FakeAchievements(ICharacter character) { Character = character; }

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public Dictionary<int, Achievement> Achievements => achievements;

		public void SetAchievement(int templateID, byte tier, uint value, bool skipEvent = false)
		{
			achievements[templateID] = new Achievement(templateID, tier, value);
		}

		public bool TryGetAchievement(int templateID, out Achievement achievement)
			=> achievements.TryGetValue(templateID, out achievement);

		public void Increment(AchievementTemplate template, uint amount)
		{
			if (template != null) { Increment(template.ID, amount); }
		}

		public void Increment(int templateID, uint amount)
		{
			if (achievements.TryGetValue(templateID, out Achievement a))
			{
				SetAchievement(templateID, a.CurrentTier, a.CurrentValue + amount);
			}
		}
	}
}
