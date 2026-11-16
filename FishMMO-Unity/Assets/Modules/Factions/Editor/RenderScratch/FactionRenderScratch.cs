using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.RenderScratch
{
	/// <summary>The Factions module's pieces of the render probes (ZZRenderScratch), registered through its hooks.</summary>
	internal static class FactionRenderScratch
	{
		[InitializeOnLoadMethod]
		private static void Install()
		{
			Rig.ExtraFakes -= AddFakes;
			Rig.ExtraFakes += AddFakes;
			Fixtures.ExtraFixtures -= Factions;
			Fixtures.ExtraFixtures += Factions;
			AllPanelsRender.ExtraPopulators["UIFactions"] = (h, d) => Panels.Character<UITKFactions>(h, d);
		}

		private static void AddFakes(ICharacter character, Dictionary<System.Type, ICharacterBehaviour> behaviours)
		{
			FakeFactions factions = new FakeFactions(character);
			Rig.Register<IFactionController>(behaviours, factions);
			Rig.Register<IFactionStandings>(behaviours, factions);
		}

		// ── Factions ────────────────────────────────────────────────

		private static void Factions(ICharacter character)
		{
			if (!character.TryGet(out IFactionController controller)) { return; }

			List<FactionTemplate> templates = Fixtures.Load<FactionTemplate>("Assets/Modules/Factions/Templates");

			// A spread across allied / neutral / hostile so all three groups render.
			int[] standings = { 8200, -4500, 0, 15000, -12000, 350, 0 };
			for (int i = 0; i < templates.Count; ++i)
			{
				controller.SetFaction(templates[i].ID, standings[i % standings.Length]);
			}
		}

	}

	/// <summary>Faction controller fake — the Factions panel reads the four dictionaries.</summary>
	public sealed class FakeFactions : IFactionStandings
	{
		private readonly Dictionary<int, Faction> all = new Dictionary<int, Faction>();
		private readonly Dictionary<int, Faction> allied = new Dictionary<int, Faction>();
		private readonly Dictionary<int, Faction> neutral = new Dictionary<int, Faction>();
		private readonly Dictionary<int, Faction> hostile = new Dictionary<int, Faction>();

		public FakeFactions(ICharacter character) { Character = character; }

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public bool IsAggressive { get; set; }
		public Dictionary<int, Faction> Factions => all;
		public Dictionary<int, Faction> Allied => allied;
		public Dictionary<int, Faction> Neutral => neutral;
		public Dictionary<int, Faction> Hostile => hostile;
		public RaceTemplate RaceTemplate { get; set; }
		public List<Trigger> OnFactionChangeTriggers { get; } = new List<Trigger>();

		public void CopyFrom(IFactionController other) { }

		public void SetFaction(int templateID, int value, bool skipEvent = false)
		{
			Faction faction = new Faction(templateID, value);
			all[templateID] = faction;

			// The panel groups by standing; mirror the sign convention the real controller uses.
			allied.Remove(templateID);
			neutral.Remove(templateID);
			hostile.Remove(templateID);
			if (value > 0) { allied[templateID] = faction; }
			else if (value < 0) { hostile[templateID] = faction; }
			else { neutral[templateID] = faction; }
		}

		public void AdjustFaction(IFactionController defender, float alliedPercent, float hostilePercent) { }

		public void Add(FactionTemplate template, int amount = 1)
		{
			if (template != null) { SetFaction(template.ID, amount); }
		}

		public FactionAllianceLevel GetAllianceLevel(IFactionController other) => FactionAllianceLevel.Neutral;
		public Color GetAllianceLevelColor(IFactionController other) => Color.white;
	}

}
