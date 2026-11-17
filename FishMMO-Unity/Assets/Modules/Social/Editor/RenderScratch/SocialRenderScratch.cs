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
	/// The Social module's part of the panel render rig: a fake friend list and its fixture, the friend list window,
	/// and the party frame in every render and probe that draws the HUD.
	/// </summary>
	public static class SocialRenderScratch
	{
		/// <summary>The party frame's layout.</summary>
		public const string PartyUxml = "Assets/Modules/Social/Client/UIParty.uxml";

		[InitializeOnLoadMethod]
		private static void Install()
		{
			Rig.ExtraFakes -= AddFakes;
			Rig.ExtraFakes += AddFakes;
			Fixtures.ExtraFixtures -= Friends;
			Fixtures.ExtraFixtures += Friends;
			AllPanelsRender.ExtraPopulators["UIFriendList"] = (h, d) => Panels.Character<UITKFriendList>(h, d);

			// The guild window, once per tab.
			AllPanelsRender.ExtraCaptures["UIGuild"] = new List<(string label, Action<GameObject, UIDocument> populate)>
			{
				("UIGuild-Roster", (h, d) => GuildPanel(h, d, "guild-tab-roster")),
				("UIGuild-Info", (h, d) => GuildPanel(h, d, "guild-tab-info")),
				("UIGuild-Log", (h, d) => GuildPanel(h, d, "guild-tab-log")),
			};

			// The party frame.
			AllPanelsRender.ExtraPopulators["UIParty"] = PartyPanel;
			if (!InterfaceScalingRender.Hud.Exists(m => m.Uxml == PartyUxml))
			{
				// Before the chat, where it always stood: later mounts draw over earlier ones.
				int chat = InterfaceScalingRender.Hud.FindIndex(m => m.PanelType == typeof(UITKChat));
				InterfaceScalingRender.Hud.Insert(chat < 0 ? InterfaceScalingRender.Hud.Count : chat,
					new InterfaceScalingRender.HudMount { Uxml = PartyUxml, Populate = PartyPanel });
			}
			if (!GridFitProbe.Windows.Exists(w => w.uxml == PartyUxml))
			{
				GridFitProbe.Windows.Add((PartyUxml, PartyPanel));
			}
			if (!HudOverlapProbe.Mounts.Exists(m => m.Name == "UIParty"))
			{
				int chat = HudOverlapProbe.Mounts.FindIndex(m => m.Name == "UIChat");
				/* IgnoreOverlap: the party frame must show EVERY member at full height and is allowed
				 * to overlap other panels to do it. It is judged on that instead — see the roster
				 * section of the log — with a full six-member party mounted. */
				HudOverlapProbe.Mounts.Insert(chat < 0 ? HudOverlapProbe.Mounts.Count : chat, new HudOverlapProbe.Mount
				{
					Name = "UIParty", Uxml = PartyUxml, Selector = "party-root", PanelType = typeof(UITKParty), IgnoreOverlap = true,
					Populate = control =>
					{
						// The largest party the server allows (PartySystem.maxPartySize).
						for (int i = 0; i < 6 && control is UITKParty party; ++i)
						{
							party.OnPartyAddMember(Seed.People[i].id, i == 0 ? PartyRank.Leader : PartyRank.Member, 1.0f);
						}
					},
				});
			}
		}

		// ── Guild ───────────────────────────────────────────────────

		public static void GuildPanel(GameObject h, UIDocument d, string tab)
		{
			UITKGuild g = h.AddComponent<UITKGuild>();
			g.Document = d;
			g.OnStarting();

			g.GuildController_OnReceiveGuildRanks(new GuildRankListBroadcast
			{
				LeaderRankOrder = 0, ViewerRankOrder = 1,
				ViewerPermissions = long.MaxValue,
				Ranks = new[]
				{
					new GuildRankEntry { RankOrder = 0, Name = "Tidecaller", Permissions = long.MaxValue },
					new GuildRankEntry { RankOrder = 1, Name = "Warden", Permissions = 0x3F },
					new GuildRankEntry { RankOrder = 2, Name = "Deepkin", Permissions = 0x0F },
					new GuildRankEntry { RankOrder = 3, Name = "Initiate", Permissions = 0x01 },
				},
			});

			g.GuildController_OnReceiveGuildInfo(Seed.GuildID, "Tideborn Covenant",
				"Raid forms at 19:00 server time. Bring tide-warding charms.",
				"Welcome to the Covenant. Read the notice, mind the reef, and never sail alone.");

			g.GuildController_OnAddMember(GMember(1001, 0, "Sunken Cathedral", "Guild lead"));
			g.GuildController_OnAddMember(GMember(1002, 1, "Coral Wastes", "Raid officer"));
			g.GuildController_OnAddMember(GMember(1003, 1, "Sunken Cathedral", "Recruitment"));
			g.GuildController_OnAddMember(GMember(1004, 2, "Tidewatch Keep", ""));
			g.GuildController_OnAddMember(GMember(1005, 2, "Coral Wastes", "Alt of Brynn"));
			g.GuildController_OnAddMember(GMember(1006, 2, "", ""));
			g.GuildController_OnAddMember(GMember(1007, 3, "", "New — needs a mentor"));
			g.GuildController_OnAddMember(GMember(1008, 3, "Tidewatch Keep", ""));

			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), hour = 3600L;
			g.GuildController_OnReceiveGuildLog(new[]
			{
				GuildLog(GuildLogEvent.Joined, 1002, 1008, "", now - hour / 4),
				GuildLog(GuildLogEvent.Promoted, 1001, 1003, "Warden", now - hour * 2),
				GuildLog(GuildLogEvent.Joined, 1003, 1007, "", now - hour * 6),
				GuildLog(GuildLogEvent.Left, 1006, 1006, "", now - hour * 30),
				GuildLog(GuildLogEvent.Demoted, 1001, 1005, "Deepkin", now - hour * 50),
			});

			// The roster view is rebuilt from OnTick, not synchronously on member-add.
			Panels.Tick(g);
			Panels.Click(d.rootVisualElement.Q<Button>(tab));
			Panels.Tick(g);
		}

		private static GuildAddBroadcast GMember(long id, byte rank, string loc, string note)
		{
			return new GuildAddBroadcast
			{
				GuildID = Seed.GuildID,
				Member = new GuildAddEntry
				{
					CharacterID = id, RankOrder = rank,
					Location = loc, RaceID = 1, PublicNote = note,
				},
			};
		}

		private static GuildLogEntry GuildLog(GuildLogEvent e, long actor, long target, string detail, long unixSeconds)
		{
			return new GuildLogEntry
			{
				Event = e, ActorCharacterID = actor, TargetCharacterID = target,
				Detail = detail, TimeUnixSeconds = unixSeconds,
			};
		}

		// ── Party ───────────────────────────────────────────────────

		public static void PartyPanel(GameObject h, UIDocument d)
		{
			UITKParty p = h.AddComponent<UITKParty>();
			p.Document = d;
			p.OnStarting();

			p.OnPartyAddMember(1001, PartyRank.Leader, 1.0f);
			p.OnPartyAddMember(1002, PartyRank.Member, 1.0f);
			p.OnPartyAddMember(1003, PartyRank.Member, 1.0f);
			p.OnPartyAddMember(1004, PartyRank.Member, 1.0f);
			p.OnPartyAddMember(1005, PartyRank.Member, 1.0f);

			p.OnPartyUpdateVitals(new[]
			{
				Vitals(1001, 0.92f, 0.64f, 0.81f, 1482.5f, 0f,
					Seed.Buff(Seed.BuffArmor, 1, 22f, 30f), Seed.Buff(Seed.BuffHaste, 2, 8f, 15f)),
				Vitals(1002, 0.47f, 0.88f, 0.55f, 210f, 2310.75f,
					Seed.Buff(Seed.BuffArmor, 1, 12f, 30f), Seed.Buff(Seed.DebuffSlow, 1, 5f, 10f)),
				Vitals(1003, 0.18f, 0.12f, 0.30f, 968.25f, 0f,
					Seed.Buff(Seed.DebuffPoison, 4, 9f, 20f), Seed.Buff(Seed.DebuffBurn, 2, 3f, 12f),
					Seed.Buff(Seed.BuffHaste, 1, 27f, 30f)),
				Vitals(1004, 1.0f, 1.0f, 0.96f, 0f, 0f),
				Vitals(1005, 0.73f, 0.41f, 0.12f, 1105f, 145.5f,
					Seed.Buff(Seed.DebuffSlow, 3, 14f, 20f)),
			});
		}

		private static PartyMemberVitalsEntry Vitals(long id, float hp, float mp, float sp,
			float dps, float hps, params ObservedBuffEntry[] buffs)
		{
			/* The fixture still speaks in fractions and rates; the payload carries them quantised
			 * (one byte per fraction, a whole-number rate) since the vitals push became change
			 * gated. Converting here keeps every caller of this helper unchanged. */
			return new PartyMemberVitalsEntry
			{
				CharacterID = id,
				HealthPCT = PartyVitalsQuantiser.FractionToByte(hp),
				ManaPCT = PartyVitalsQuantiser.FractionToByte(mp),
				StaminaPCT = PartyVitalsQuantiser.FractionToByte(sp),
				DamagePerSecond = PartyVitalsQuantiser.RateToUInt16(dps),
				HealPerSecond = PartyVitalsQuantiser.RateToUInt16(hps),
				BuffsChanged = true,
				Buffs = buffs,
			};
		}

		private static void AddFakes(ICharacter character, Dictionary<Type, ICharacterBehaviour> behaviours)
		{
			Rig.Register<IFriendController>(behaviours, new FakeFriends(character));
		}

		// ── Friends ─────────────────────────────────────────────────

		private static void Friends(ICharacter character)
		{
			if (!character.TryGet(out IFriendController controller)) { return; }
			foreach ((long id, string _) in Seed.People)
			{
				if (id != 1001) { controller.AddFriend(id); }
			}
		}
	}

	/// <summary>Friend controller fake — the friend list reads <c>Friends</c>.</summary>
	public sealed class FakeFriends : IFriendController
	{
		public FakeFriends(ICharacter character) { Character = character; }

		public ICharacter Character { get; private set; }
		public bool Initialized => true;
		public void InitializeOnce(ICharacter character) { Character = character; }
		public void OnStartCharacter() { }
		public void OnStopCharacter() { }

		public event Action<long, bool> OnAddFriend;
		public event Action<long> OnRemoveFriend;

		public HashSet<long> Friends { get; } = new HashSet<long>();

		public void AddFriend(long friendID)
		{
			if (Friends.Add(friendID)) { OnAddFriend?.Invoke(friendID, true); }
		}

		public void RemoveFriend(long friendID)
		{
			if (Friends.Remove(friendID)) { OnRemoveFriend?.Invoke(friendID); }
		}
	}
}
