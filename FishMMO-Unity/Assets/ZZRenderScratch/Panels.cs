using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>Shared fabricated world state for the render harness.</summary>
	public static class Seed
	{
		public const long GuildID = 900001;

		public static readonly (long id, string name)[] People =
		{
			(1001, "Thalorin"), (1002, "Brynn"), (1003, "Kaelen Duskwater"), (1004, "Mira"),
			(1005, "Sorrel"), (1006, "Vaskir"), (1007, "Elowen"), (1008, "Draeven"),
		};

		public static int BuffArmor, BuffHaste, DebuffPoison, DebuffSlow, DebuffBurn;

		public static void All()
		{
			Names();
			Buffs();
		}

		private static void Names()
		{
			FieldInfo field = typeof(ClientNamingSystem).GetField("idToName",
				BindingFlags.NonPublic | BindingFlags.Static);
			var map = (Dictionary<NamingSystemType, Dictionary<long, string>>)field.GetValue(null);

			if (!map.TryGetValue(NamingSystemType.CharacterName, out var chars))
			{
				map[NamingSystemType.CharacterName] = chars = new Dictionary<long, string>();
			}
			foreach ((long id, string name) in People) { chars[id] = name; }

			if (!map.TryGetValue(NamingSystemType.GuildName, out var guilds))
			{
				map[NamingSystemType.GuildName] = guilds = new Dictionary<long, string>();
			}
			guilds[GuildID] = "Tideborn Covenant";
		}

		/// <remarks>
		/// AddToCache is not called on load — the shipped path is an addressables loader at boot,
		/// so an asset pulled from the AssetDatabase has ID 0 and never enters the lookup.
		/// The project ships no debuff templates, so those are created in memory.
		/// </remarks>
		private static void Buffs()
		{
			BaseBuffTemplate armor = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(
				"Assets/Templates/Entity/Buffs/Minor Increase Armor.asset");
			BaseBuffTemplate haste = AssetDatabase.LoadAssetAtPath<BaseBuffTemplate>(
				"Assets/Templates/Entity/Buffs/Minor Increase Move Speed.asset");

			if (armor != null) { armor.AddToCache(armor.name); BuffArmor = armor.ID; }
			if (haste != null) { haste.AddToCache(haste.name); BuffHaste = haste.ID; }

			DebuffPoison = Debuff("Creeping Venom");
			DebuffSlow = Debuff("Mirebound");
			DebuffBurn = Debuff("Emberbrand");
		}

		private static int Debuff(string name)
		{
			AttributeBuffTemplate t = ScriptableObject.CreateInstance<AttributeBuffTemplate>();
			t.name = name;
			t.hideFlags = HideFlags.HideAndDontSave;
			t.IsDebuff = true;
			t.AddToCache(name);
			return t.ID;
		}

		public static ObservedBuffEntry Buff(int id, int stacks, float remaining, float total)
		{
			return new ObservedBuffEntry
			{
				TemplateID = id, Stacks = stacks, RemainingSeconds = remaining, TotalSeconds = total,
			};
		}
	}

	/// <summary>Per-panel populators, each driving the panel through its own public seam.</summary>
	public static class Panels
	{
		public static readonly string[] OptionsTabs =
		{
			"display", "audio", "gameplay", "controls", "interface",
		};

		private static void Click(Button b)
		{
			if (b == null) return;
			using (NavigationSubmitEvent e = NavigationSubmitEvent.GetPooled())
			{
				e.target = b;
				b.SendEvent(e);
			}
		}

		private static void Tick(object panel)
		{
			MethodInfo m = panel.GetType().GetMethod("OnTick",
				BindingFlags.NonPublic | BindingFlags.Instance);
			m?.Invoke(panel, null);
		}

		// ── Party ───────────────────────────────────────────────────

		public static void Party(GameObject h, UIDocument d)
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

		// ── Guild ───────────────────────────────────────────────────

		public static void Guild(GameObject h, UIDocument d, string tab)
		{
			UITKGuild g = h.AddComponent<UITKGuild>();
			g.Document = d;
			g.OnStarting();

			g.GuildController_OnReceiveGuildRanks(new GuildRankListBroadcast
			{
				GuildID = Seed.GuildID, LeaderRankOrder = 0, ViewerRankOrder = 1,
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

			g.GuildController_OnAddMember(GMember(1001, 0, "Sunken Cathedral", 60, "Guild lead"));
			g.GuildController_OnAddMember(GMember(1002, 1, "Coral Wastes", 58, "Raid officer"));
			g.GuildController_OnAddMember(GMember(1003, 1, "Sunken Cathedral", 57, "Recruitment"));
			g.GuildController_OnAddMember(GMember(1004, 2, "Tidewatch Keep", 52, ""));
			g.GuildController_OnAddMember(GMember(1005, 2, "Coral Wastes", 49, "Alt of Brynn"));
			g.GuildController_OnAddMember(GMember(1006, 2, "", 44, ""));
			g.GuildController_OnAddMember(GMember(1007, 3, "", 21, "New — needs a mentor"));
			g.GuildController_OnAddMember(GMember(1008, 3, "Tidewatch Keep", 17, ""));

			long now = DateTime.UtcNow.Ticks, hour = TimeSpan.TicksPerHour;
			g.GuildController_OnReceiveGuildLog(Seed.GuildID, new[]
			{
				Log(GuildLogEvent.Joined, 1002, 1008, "", now - hour / 4),
				Log(GuildLogEvent.Promoted, 1001, 1003, "Warden", now - hour * 2),
				Log(GuildLogEvent.Joined, 1003, 1007, "", now - hour * 6),
				Log(GuildLogEvent.Left, 1006, 1006, "", now - hour * 30),
				Log(GuildLogEvent.Demoted, 1001, 1005, "Deepkin", now - hour * 50),
			});

			// The roster view is rebuilt from OnTick, not synchronously on member-add.
			Tick(g);
			Click(d.rootVisualElement.Q<Button>(tab));
			Tick(g);
		}

		private static GuildAddBroadcast GMember(long id, byte rank, string loc, int level, string note)
		{
			return new GuildAddBroadcast
			{
				GuildID = Seed.GuildID, CharacterID = id, RankOrder = rank,
				Location = loc, RaceID = 1, Level = level, PublicNote = note,
			};
		}

		private static GuildLogEntry Log(GuildLogEvent e, long actor, long target, string detail, long ticks)
		{
			return new GuildLogEntry
			{
				Event = e, ActorCharacterID = actor, TargetCharacterID = target,
				Detail = detail, TimeUtcTicks = ticks,
			};
		}

		// ── Options ─────────────────────────────────────────────────

		public static void Options(GameObject h, UIDocument d, string tab)
		{
			Options(h, d, tab, scrollToEnd: false);
		}

		/// <param name="scrollToEnd">
		/// True to park the tab's scroll view at the bottom. The UI tab's profile controls — the
		/// half of it that saves and loads a shared layout — sit below the fold at this panel
		/// size, so a capture from the top never shows them at all.
		/// </param>
		public static void Options(GameObject h, UIDocument d, string tab, bool scrollToEnd)
		{
			UITKOptions o = h.AddComponent<UITKOptions>();
			o.Document = d;
			o.OnStarting();

			VisualElement root = d.rootVisualElement;
			foreach (string t in OptionsTabs)
			{
				VisualElement page = root.Q("options-page-" + t);
				page?.EnableInClassList("options-page--hidden", t != tab);
				root.Q<Button>("options-tab-" + t)?.EnableInClassList("fish-tab--active", t == tab);
			}

			if (scrollToEnd && root.Q("options-page-" + tab) is ScrollView view)
			{
				/* Deferred: the page was hidden a moment ago and has no resolved content height
				 * yet, so a scroll issued now would be clamped to zero. */
				view.RegisterCallback<GeometryChangedEvent>(_ =>
					view.scrollOffset = new Vector2(0.0f, float.MaxValue));
			}
		}

		// ── Shared panels ───────────────────────────────────────────

		public static void Chat(GameObject h, UIDocument d)
		{
			UITKChat c = h.AddComponent<UITKChat>();
			c.Document = d;
			c.OnStarting();

			MethodInfo received = typeof(UITKChat).GetMethod("OnClientChatBroadcastReceived",
				BindingFlags.NonPublic | BindingFlags.Instance);

			(ChatChannel ch, long sender, string text)[] lines =
			{
				(ChatChannel.Say,     1002, "Anyone up for the Sunken Cathedral run?"),
				(ChatChannel.Party,   1001, "Give me two minutes, repairing."),
				(ChatChannel.Guild,   1003, "Raid forms at 19:00 — read the notice."),
				(ChatChannel.Say,     1004, "Where does the tide-warding charm drop?"),
				(ChatChannel.Guild,   1005, "Reef wing, second boss. Rare-ish."),
				(ChatChannel.Tell,    1002, "Invite me when you're ready?"),
				(ChatChannel.World,   1008, "Selling Emberbrand scrolls, whisper me."),
				(ChatChannel.Party,   1001, "Ready. Pulling."),
			};

			long now = DateTime.UtcNow.Ticks;
			for (int i = 0; i < lines.Length; ++i)
			{
				ChatBroadcast msg = new ChatBroadcast
				{
					Channel = lines[i].ch,
					SenderID = lines[i].sender,
					Text = lines[i].text,
					ReceivedUtcTicks = now - (lines[i].Length() * 0),
				};
				if (received != null)
				{
					/* The second parameter is FishNet's Channel enum. Its default is built from the
					 * signature so this assembly needs no FishNet reference. */
					Type channelType = received.GetParameters()[1].ParameterType;
					object channel = channelType.IsValueType ? Activator.CreateInstance(channelType) : null;
					received.Invoke(c, new object[] { msg, channel });
				}
			}
		}

		private static int Length(this (ChatChannel ch, long sender, string text) t) => 0;

		public static void Tooltip(GameObject h, UIDocument d)
		{
			UITKTooltip t = h.AddComponent<UITKTooltip>();
			t.Document = d;
			t.OnStarting();
			t.Open("<size=120%>Tideward Charm</size>\nBinds a fragment of the reef to its bearer.\n" +
				"<color=#7FC9FF>+14 Armor</color>\n<color=#7FC9FF>+6 Tide Resistance</color>\n" +
				"Requires level 40.");
		}

		public static void ContextMenu(GameObject h, UIDocument d)
		{
			UITKContextMenu m = h.AddComponent<UITKContextMenu>();
			m.Document = d;
			m.OnStarting();
			m.Open(new List<(string, Action)>
			{
				("Inspect", () => { }),
				("Add Friend", () => { }),
				("Invite to Party", () => { }),
				("Invite to Guild", () => { }),
				("Trade", () => { }),
				("Report", () => { }),
			});
		}

		public static void Dropdown(GameObject h, UIDocument d)
		{
			UITKDropdown dd = h.AddComponent<UITKDropdown>();
			dd.Document = d;
			dd.OnStarting();
			dd.AddButton("Equip", () => { });
			dd.AddButton("Split Stack", () => { });
			dd.AddButton("Drop", () => { });
			dd.AddToggle("Lock Slot", (_) => { }, true);
		}

		public static void DialogBox(GameObject h, UIDocument d)
		{
			UITKDialogBox b = h.AddComponent<UITKDialogBox>();
			b.Document = d;
			b.OnStarting();
			b.SetText("Delete the UI profile 'Raid Layout'?\n\nThis cannot be undone.");
		}

		public static void ColorPicker(GameObject h, UIDocument d)
		{
			UITKColorPicker p = h.AddComponent<UITKColorPicker>();
			p.Document = d;
			p.OnStarting();
			p.Open(new Color32(0, 115, 192, 255), (_) => { });
		}

		public static void LoadingScreen(GameObject h, UIDocument d)
		{
			UITKLoadingScreen s = h.AddComponent<UITKLoadingScreen>();
			s.Document = d;
			s.OnStarting();
			s.OnProgressUpdate(0.62f);
		}

		// ── Character-driven panels ─────────────────────────────────

		/// <summary>
		/// Builds the rigged character on the panel's own host and hands it over.
		/// </summary>
		/// <remarks>
		/// The panels are <c>UITKCharacterControl</c> subclasses: they read everything through
		/// <c>Character.TryGet&lt;TController&gt;()</c>, so the character has to exist and be set
		/// before OnStarting runs.
		/// </remarks>
		private static T Character<T>(GameObject h, UIDocument d) where T : UITKCharacterControl
		{
			PlayerCharacter character = Rig.Build(h);

			T panel = h.AddComponent<T>();
			panel.Document = d;
			panel.SetCharacter(character);
			panel.OnStarting();
			Tick(panel);
			return panel;
		}

		/// <summary>Invokes one of the panel's private broadcast handlers.</summary>
		/// <remarks>
		/// These panels are driven entirely by server broadcasts, and the handlers are private —
		/// the same shape as Chat. The FishNet Channel argument is built from the method's own
		/// signature so this assembly needs no FishNet reference for it.
		/// </remarks>
		private static void Broadcast(object panel, string handler, object message)
		{
			MethodInfo m = panel.GetType().GetMethod(handler,
				BindingFlags.NonPublic | BindingFlags.Instance);
			if (m == null) { throw new InvalidOperationException(handler + " not found"); }

			Type channelType = m.GetParameters()[1].ParameterType;
			object channel = channelType.IsValueType ? Activator.CreateInstance(channelType) : null;
			m.Invoke(panel, new object[] { message, channel });
		}

		/// <summary>
		/// Invokes a handler that answers by sending a broadcast of its own.
		/// </summary>
		/// <remarks>
		/// <c>Client.Broadcast</c> is static and goes straight to <c>NetworkManager.ClientManager</c>,
		/// which no edit-mode process has. The handler's own work — state, layout, the calls into
		/// ApplyList/ApplyControls — has all run by the time it reaches that line, so the send is
		/// swallowed and nothing else is. <paramref name="sendSite"/> names the method expected to
		/// send, and only a NullReferenceException raised there is tolerated, so a genuine fault
		/// anywhere else in the handler still fails the capture.
		/// </remarks>
		private static void BroadcastOffline(object panel, string handler, object message, string sendSite)
		{
			try
			{
				Broadcast(panel, handler, message);
			}
			catch (Exception ex)
			{
				Exception root = ex;
				while (root.InnerException != null) { root = root.InnerException; }

				/* Matched against the method that sends, not against Client.Broadcast itself:
				 * that one is AggressiveInlining, so it never appears in the stack at all. */
				bool outbound = root is NullReferenceException
					&& root.StackTrace != null
					&& root.StackTrace.Contains(sendSite);
				if (!outbound) { throw; }

				Debug.Log("[Panels] " + handler + ": outbound send from " + sendSite +
					" skipped (no NetworkManager in edit mode).");
			}
		}

		/// <summary>Dungeon Finder showing a populated instance list at a chosen difficulty.</summary>
		public static void DungeonFinder(GameObject h, UIDocument d)
		{
			UITKDungeonFinder panel = Character<UITKDungeonFinder>(h, d);
			DungeonTemplate dungeon = SeedDungeon();

			// The entrance the player interacted with, then the list the server answers with.
			BroadcastOffline(panel, "OnClientDungeonFinderBroadcastReceived", new DungeonFinderBroadcast
			{
				InteractableID = 77001,
				DungeonTemplateID = dungeon != null ? dungeon.ID : 0,
			}, sendSite: "UITKDungeonFinder.RequestList");

			/* The panel resets to the first difficulty on every open, and discards a reply whose
			 * difficulty is not the one it is showing. Echo back whatever it settled on rather
			 * than naming an index and having the list silently dropped. */
			int difficulty = Read<int>(panel, "selectedDifficulty");

			Broadcast(panel, "OnClientDungeonFinderListResultBroadcastReceived",
				new DungeonFinderListResultBroadcast
				{
					InteractableID = 77001,
					Difficulty = difficulty,
					Reason = DungeonListFailureReason.None,
					Instances = new[]
					{
						Instance(88001, "Brynn", 4, 5, 1820, false, true),
						Instance(88002, "Kaelen Duskwater", 5, 5, 640, false, false),
						Instance(88003, "Vaskir", 2, 5, 3540, true, false),
						Instance(88004, "Elowen", 1, 5, 2995, false, false),
					},
				});

			Tick(panel);
		}

		/// <summary>Reads a private field the panel keeps its state in.</summary>
		private static T Read<T>(object target, string field)
		{
			FieldInfo f = target.GetType().GetField(field,
				BindingFlags.NonPublic | BindingFlags.Instance);
			return f != null && f.GetValue(target) is T value ? value : default;
		}

		/// <summary>
		/// Builds a dungeon for the finder to describe, and registers it the way the boot loader would.
		/// </summary>
		/// <remarks>
		/// The project ships no authored DungeonTemplate asset yet, and an entrance with no template
		/// renders as a single unnamed difficulty — which would exercise none of the tab strip. This
		/// is a stand-in built in memory so the header, the description and a multi-difficulty strip
		/// are all on screen.
		/// </remarks>
		private static DungeonTemplate SeedDungeon()
		{
			DungeonTemplate dungeon = ScriptableObject.CreateInstance<DungeonTemplate>();
			dungeon.hideFlags = HideFlags.HideAndDontSave;
			dungeon.name = "Sunken Cathedral";
			dungeon.DisplayName = "The Sunken Cathedral";
			dungeon.DungeonSceneName = "SunkenCathedral";
			dungeon.Description =
				"Tidewater took the nave a century ago. What still sings the offices down there " +
				"has not been human for longer than that.";
			dungeon.Difficulties = new List<DungeonDifficultyDefinition>
			{
				new DungeonDifficultyDefinition
				{
					Name = "Normal", MinimumPartySize = 1, MaximumPlayers = 5,
					LifetimeMinutes = 60, AllowResurrection = true,
				},
				new DungeonDifficultyDefinition
				{
					Name = "Heroic", MinimumPartySize = 3, MaximumPlayers = 5,
					EnemyResourceMultiplier = 1.75f, LootQuantityMultiplier = 1.5f,
					CurrencyMultiplier = 1.5f, LifetimeMinutes = 90, AllowResurrection = true,
				},
				new DungeonDifficultyDefinition
				{
					Name = "Mythic", MinimumPartySize = 5, MaximumPlayers = 5,
					EnemyResourceMultiplier = 3.0f, LootQuantityMultiplier = 2.5f,
					CurrencyMultiplier = 2.0f, LifetimeMinutes = 120,
					LivesPerCharacter = 1, AllowResurrection = false,
				},
			};

			dungeon.AddToCache(dungeon.name);
			return dungeon;
		}

		private static DungeonInstanceEntry Instance(long id, string leader, int members, int max,
			int remaining, bool loading, bool ownParty)
		{
			return new DungeonInstanceEntry
			{
				InstanceID = id, LeaderName = leader, MemberCount = members, MaxMembers = max,
				RemainingSeconds = remaining, IsLoading = loading, IsOwnParty = ownParty,
			};
		}

		/// <summary>Instance panel from inside a running instance, as its leader.</summary>
		public static void InstancePanel(GameObject h, UIDocument d)
		{
			UITKInstance panel = Character<UITKInstance>(h, d);

			Broadcast(panel, "OnClientInstanceDetailsBroadcastReceived", new InstanceDetailsBroadcast
			{
				InInstance = true,
				SceneName = "Sunken Cathedral",
				DifficultyName = "Heroic",
				RemainingSeconds = 1820,
				LeaderCharacterID = 1001,
				LeaderName = "Thalorin",
				ViewerIsLeader = true,
				IsPrivate = false,
				Members = new[]
				{
					Member(1001, "Thalorin", true, true),
					Member(1002, "Brynn", false, false),
					Member(1003, "Kaelen Duskwater", false, false),
					Member(1004, "Mira", false, false),
					Member(1005, "Sorrel", false, false),
				},
			});

			Tick(panel);
		}

		private static InstanceMemberData Member(long id, string name, bool leader, bool self)
		{
			return new InstanceMemberData
			{
				CharacterID = id, Name = name, IsLeader = leader, IsSelf = self,
			};
		}

		public static void Inventory(GameObject h, UIDocument d) => Character<UITKInventory>(h, d);
		public static void Equipment(GameObject h, UIDocument d) => Character<UITKEquipment>(h, d);
		public static void Bank(GameObject h, UIDocument d) => Character<UITKBank>(h, d);
		public static void Achievements(GameObject h, UIDocument d) => Character<UITKAchievements>(h, d);
		public static void Factions(GameObject h, UIDocument d) => Character<UITKFactions>(h, d);
		public static void FriendList(GameObject h, UIDocument d) => Character<UITKFriendList>(h, d);

		public static void DeathDialog(GameObject h, UIDocument d)
		{
			UITKDeathDialog dd = h.AddComponent<UITKDeathDialog>();
			dd.Document = d;
			dd.OnStarting();
			dd.ShowDeathDialog();
		}
	}
}
