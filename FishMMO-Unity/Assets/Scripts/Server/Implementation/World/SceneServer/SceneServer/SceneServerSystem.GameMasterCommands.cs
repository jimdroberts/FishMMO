using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The in-game <c>/gm</c> command set: finding players, moving them, talking to them,
	/// moderating them, and working the support queue.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Registered once, as <c>/gm</c>, at <see cref="AccessLevel.GameMaster"/>. The sub-command
	/// is the first word of the remainder and is looked up in one table, so one access check
	/// covers every operation — the same shape as <c>/admin</c>, and for the same reason: there
	/// is no way to add a sub-command that forgets to be gated. See
	/// <c>SceneServerSystem.OperatorCommands</c>.
	/// </para>
	/// <para>
	/// Every command here is recorded in the operator audit log, including refusals, by the
	/// chat system's subscription to the access gate. Nothing in these files writes an audit row,
	/// and nothing should: a command audited because its author remembered is a command that stops
	/// being audited the day somebody forgets.
	/// </para>
	/// <para>
	/// <b>What is deliberately not here.</b> Nothing that creates items or currency, changes an
	/// attribute, heals, revives or kills, or otherwise changes the game a player is playing — a
	/// game master account that can do those is an exploit waiting for a compromised password, and
	/// they belong to <c>/admin</c>. Nothing that grants access, and no permanent ban: a game
	/// master's ban is capped and lapses on its own. A test pins that these files call nothing
	/// declared in the administrator files.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>Most characters <c>/gm who</c> will name before it stops listing.</summary>
		/// <remarks>
		/// A populated scene server holds far more than a chat window can show, and a reply per
		/// character would flood the operator out of their own log. The count is always reported
		/// even when the list is cut; the staff console lists everyone.
		/// </remarks>
		private const int MaxWhoListed = 20;

		/// <summary>Largest coordinate <c>/gm gotopos</c> accepts on any axis.</summary>
		private const float MaxTeleportCoordinate = 100000f;

		/// <summary>Most summon return points kept before the stale ones are pruned.</summary>
		private const int MaxSummonReturnPoints = 512;

		/// <summary>Where a summoned character stood before they were summoned.</summary>
		private struct SummonReturnPoint
		{
			/// <summary>The scene instance they were in, by handle.</summary>
			public int SceneHandle;

			/// <summary>Where they stood.</summary>
			public Vector3 Position;

			/// <summary>Which way they faced.</summary>
			public Quaternion Rotation;
		}

		/// <summary>Pre-summon positions, by character id, for <c>/gm return</c>.</summary>
		/// <remarks>
		/// The FIRST summon's origin is kept across repeated summons. Summoning somebody twice and
		/// then sending them back should send them home, not to wherever the first summon left them.
		/// </remarks>
		private readonly Dictionary<long, SummonReturnPoint> summonReturnPoints = new Dictionary<long, SummonReturnPoint>();

		/// <summary>Registers the <c>/gm</c> command and the staff console requests.</summary>
		private void RegisterGameMasterCommands()
		{
			gameMasterCommands = new OperatorCommandSet("/gm", AccessLevel.GameMaster, BuildGameMasterCommands());

			ChatHelper.AddCommands(new Dictionary<string, ChatCommand>()
			{
				{ "/gm", OnGameMasterCommand },
			}, AccessLevel.GameMaster);

			ChatHelper.SetCommandHelp("/gm", new ChatCommandHelp()
			{
				Category = "Staff",
				Arguments = "<command>",
				Summary = "Game master commands. /gm help lists them.",
			});

			ChatHelper.SetAuditRedactor("/gm", RedactGameMasterAudit);

			RegisterStaffConsoleBroadcasts();
		}

		/// <summary>Unregisters the <c>/gm</c> command and the staff console requests.</summary>
		private void UnregisterGameMasterCommands()
		{
			UnregisterStaffConsoleBroadcasts();
			ChatHelper.RemoveCommands(new[] { "/gm" });
			summonReturnPoints.Clear();
		}

		/// <summary>Dispatches a <c>/gm</c> sub-command.</summary>
		private bool OnGameMasterCommand(IPlayerCharacter character, ChatBroadcast msg)
		{
			return DispatchOperatorCommand(gameMasterCommands, character, msg);
		}

		/// <summary>The whole <c>/gm</c> table, in presentation order.</summary>
		private IEnumerable<OperatorCommand> BuildGameMasterCommands()
		{
			var commands = new List<OperatorCommand>()
			{
				new OperatorCommand
				{
					Name = "who", Aliases = new[] { "online" }, Category = "Players",
					Summary = "Lists the characters this scene server holds.",
					Run = (c, a) => ReportWho(c),
				},
				new OperatorCommand
				{
					Name = "where", Aliases = new[] { "find" }, Category = "Players",
					Summary = "Shows the scene and coordinates of a character on this scene server.",
					Arguments = "character:Character", RosterAction = true,
					Run = ReportWhere,
				},
				new OperatorCommand
				{
					Name = "info", Category = "Players",
					Summary = "Shows a character's account, access level, scene, and whether they are dead, fighting or muted.",
					Arguments = "character:Character", RosterAction = true,
					Run = ReportCharacterInfo,
				},
				new OperatorCommand
				{
					Name = "seen", Category = "Players",
					Summary = "Looks a character up in the database whether or not they are online, anywhere in the shard.",
					Arguments = "character:Character", RosterAction = true,
					Run = ReportSeen,
				},
				new OperatorCommand
				{
					Name = "chars", Category = "Players",
					Summary = "Lists the characters on an account.",
					Arguments = "account:Account",
					Run = ReportAccountCharacters,
				},
				new OperatorCommand
				{
					Name = "account", Category = "Players",
					Summary = "Shows an account's standing: access level, sign-in history, ban and mute.",
					Arguments = "account:Account",
					Run = ReportAccount,
				},

				new OperatorCommand
				{
					Name = "goto", Aliases = new[] { "tp" }, Category = "Movement",
					Summary = "Moves you to a character in your scene.",
					Arguments = "character:Character", RosterAction = true,
					Run = GoToCharacter,
				},
				new OperatorCommand
				{
					Name = "summon", Aliases = new[] { "bring" }, Category = "Movement",
					Summary = "Moves a character in your scene to you. They are told who summoned them.",
					Arguments = "character:Character", RosterAction = true,
					Run = SummonCharacter,
				},
				new OperatorCommand
				{
					Name = "return", Category = "Movement",
					Summary = "Sends a summoned character back to where they stood before the first summon.",
					Arguments = "character:Character", RosterAction = true,
					Run = ReturnSummonedCharacter,
				},
				new OperatorCommand
				{
					Name = "gotopos", Category = "Movement",
					Summary = "Moves you to coordinates in the scene you are standing in.",
					Arguments = "x:Number;y:Number;z:Number",
					Run = GoToPosition,
				},
				new OperatorCommand
				{
					Name = "unstuck", Category = "Movement",
					Summary = "Moves a character to the nearest respawn point in the scene they are in.",
					Arguments = "character:Character", RosterAction = true,
					Run = UnstickCharacter,
				},
				new OperatorCommand
				{
					Name = "rescue", Category = "Movement",
					Summary = "Moves a character to a respawn point, spawn point or waypoint in their scene. Nearest if none is named.",
					Arguments = "character:Character;to:Choice=respawn,spawn,waypoint;point:Text?", RosterAction = true,
					Run = RescueCharacter,
				},
				new OperatorCommand
				{
					Name = "points", Category = "Movement",
					Summary = "Lists the respawn points, spawn points and waypoints a rescue can use in a character's scene.",
					Arguments = "character:Character?",
					Run = ListRescuePoints,
				},
			};

			commands.AddRange(BuildGameMasterModerationCommands());
			commands.AddRange(BuildGameMasterSupportCommands());
			commands.AddRange(BuildGameMasterWorldCommands());

			commands.Add(new OperatorCommand
			{
				Name = "console", Aliases = new[] { "panel" }, Category = "Console",
				Summary = "Opens the staff console.",
				Run = (c, a) => SendStaffConsoleCatalog(c, open: true),
			});

			return commands;
		}

		#region Lookups

		/// <summary>Lists the characters this scene server currently holds.</summary>
		private void ReportWho(IPlayerCharacter character)
		{
			if (!TryGetOnlineCharacters(out var mapping))
			{
				Reply(character, "The character mapping is unavailable.");
				return;
			}

			var names = mapping.CharactersByID.Values
				.Where(c => c != null)
				.Select(c => c.CharacterName)
				.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
				.ToList();

			Reply(character, $"{names.Count} character(s) on this scene server.");
			if (names.Count == 0)
			{
				return;
			}

			/* Batched into lines rather than one reply per name: twenty replies is twenty
			 * broadcasts and scrolls the operator's own chat away. */
			ReplyLines(character, OperatorCommandParsing.PackLines(names.Take(MaxWhoListed), string.Empty, ", ", OperatorLineLength));
			if (names.Count > MaxWhoListed)
			{
				Reply(character, $"...and {names.Count - MaxWhoListed} more. Narrow it with /gm where <name>, or open /gm console.");
			}
		}

		/// <summary>Reports where a character is, if this scene server holds them.</summary>
		private void ReportWhere(IPlayerCharacter character, string arguments)
		{
			/* reads the target's location and changes nothing about them, so it is not an escalation to run it on a peer. */
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target,
				StaffTargetRank.SkipOutranks))
			{
				return;
			}

			Vector3 position = target.Transform != null ? target.Transform.position : Vector3.zero;
			Reply(character, $"{target.CharacterName} is in '{target.CurrentSceneName()}' at " +
				$"{position.x.ToString("0.0", CultureInfo.InvariantCulture)}, " +
				$"{position.y.ToString("0.0", CultureInfo.InvariantCulture)}, " +
				$"{position.z.ToString("0.0", CultureInfo.InvariantCulture)}.");
		}

		/// <summary>Reports a character's account and standing.</summary>
		/// <remarks>
		/// Account name, access level and state, and nothing else. A game master needs to know who
		/// they are dealing with; they do not need the account's email address, and a command that
		/// prints it makes every game master a place that address can leak from.
		/// </remarks>
		private void ReportCharacterInfo(IPlayerCharacter character, string arguments)
		{
			/* reads the target's details and changes nothing about them, so it is not an escalation to run it on a peer. */
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target,
				StaffTargetRank.SkipOutranks))
			{
				return;
			}

			Reply(character, $"{target.CharacterName} (id {target.ID}) on account '{target.Account}'.");
			Reply(character, $"Access {target.AccessLevel}, scene '{target.CurrentSceneName()}', world {target.WorldServerID}.");
			Reply(character, $"{(target.IsFlagged(CharacterFlags.IsDead) ? "Dead" : "Alive")}, " +
				$"{(target.IsFlagged(CharacterFlags.IsInCombat) ? "in combat" : "not in combat")}, " +
				$"{DescribeChatMute(target.ChatMutedUntilTicks)}.");
		}

		/// <summary>Looks a character up in the database, online or not.</summary>
		private void ReportSeen(IPlayerCharacter character, string arguments)
		{
			string name = OperatorCommandParsing.SplitFirstWord(arguments, out _);
			if (name.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, "seen");
				return;
			}

			RunOperatorLines(character, async () =>
			{
				if (!TryGetDbService(out ICharacterService characterService))
				{
					return new[] { "The character service is unavailable." };
				}

				DatabaseResult<CharacterData?> found = await characterService.FetchAsync(name, null);
				if (!found.IsSuccess)
				{
					return new[] { $"Could not look '{OperatorCommandParsing.Truncate(name, 32)}' up: {found.ErrorMessage}" };
				}
				if (found.Data == null)
				{
					return new[] { $"No character named '{OperatorCommandParsing.Truncate(name, 32)}'." };
				}

				CharacterData data = (CharacterData)found.Data;
				var lines = new List<string>()
				{
					$"{data.Name} (id {data.ID}) on account '{data.Account}', {(data.Online ? "ONLINE" : "offline")}.",
					$"World {data.WorldServerID}, scene '{data.SceneName}', last saved {DescribeSince(data.LastSaved)}.",
				};

				DatabaseResult<CharacterChatMuteState> mute = await characterService.FetchChatMuteAsync(data.ID);
				if (mute.IsSuccess)
				{
					lines.Add(DescribeStoredMute("Character", mute.Data.Character));
					lines.Add(DescribeStoredMute("Account", mute.Data.Account));
				}
				return lines;
			});
		}

		/// <summary>Lists the characters on an account.</summary>
		private void ReportAccountCharacters(IPlayerCharacter character, string arguments)
		{
			string account = OperatorCommandParsing.SplitFirstWord(arguments, out _);
			if (account.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, "chars");
				return;
			}

			RunOperatorLines(character, async () =>
			{
				if (!TryGetDbService(out ICharacterService characterService))
				{
					return new[] { "The character service is unavailable." };
				}

				DatabaseResult<IReadOnlyList<CharacterAdminData>> result =
					await characterService.FetchAdminByAccountAsync(account, includeDeleted: false);
				if (!result.IsSuccess)
				{
					return new[] { $"Could not read '{OperatorCommandParsing.Truncate(account, 32)}': {result.ErrorMessage}" };
				}

				IReadOnlyList<CharacterAdminData> rows = result.Data ?? Array.Empty<CharacterAdminData>();
				if (rows.Count == 0)
				{
					return new[] { $"'{OperatorCommandParsing.Truncate(account, 32)}' has no characters, or does not exist." };
				}

				var lines = new List<string>() { $"'{account}' has {rows.Count} character(s):" };
				lines.AddRange(OperatorCommandParsing.PackLines(
					rows.Select(r => $"{r.Name} ({(r.SessionState != 0 ? "online" : "offline")}, {r.SceneName})"),
					string.Empty, "; ", OperatorLineLength));
				return lines;
			});
		}

		/// <summary>Reports an account's standing.</summary>
		/// <remarks>
		/// No email address, for the same reason <c>info</c> prints none. Ban and mute are shown with
		/// who applied them and why, because the game master reading this is usually answering a
		/// player who wants to know exactly that.
		/// </remarks>
		private void ReportAccount(IPlayerCharacter character, string arguments)
		{
			string account = OperatorCommandParsing.SplitFirstWord(arguments, out _);
			if (account.Length == 0)
			{
				ReplyUsage(character, gameMasterCommands, "account");
				return;
			}

			RunOperatorLines(character, async () =>
			{
				if (!TryGetDbService(out IAccountService accountService))
				{
					return new[] { "The account service is unavailable." };
				}

				DatabaseResult<AccountAdminData> result = await accountService.FetchAdminAsync(account);
				if (!result.IsSuccess || result.Data == null)
				{
					return new[] { $"No account named '{OperatorCommandParsing.Truncate(account, 32)}'." };
				}

				AccountAdminData data = result.Data;
				var level = (AccessLevel)data.AccessLevel;
				var lines = new List<string>()
				{
					$"'{data.Name}': {level}, created {data.Created:yyyy-MM-dd}, last signed in {DescribeSince(data.LastLogin)}.",
					$"{data.CharacterCount} character(s), email {(data.Verified ? "verified" : "unverified")}, two-factor {(data.TotpEnabled ? "on" : "off")}.",
				};

				if (level == AccessLevel.Banned)
				{
					string span = data.BannedUntil == null
						? "permanently"
						: $"until {data.BannedUntil.Value:yyyy-MM-dd HH:mm} UTC";
					lines.Add($"BANNED {span}{(string.IsNullOrEmpty(data.BannedBy) ? string.Empty : " by " + data.BannedBy)}: {data.BanReason ?? "no reason recorded"}");
				}

				lines.Add(DescribeStoredMute("Account", new ChatMuteData(data.Muted, data.MutedUntil, data.MutedBy, data.MuteReason)));
				return lines;
			});
		}

		/// <summary>Describes a stored mute in one line.</summary>
		private static string DescribeStoredMute(string scope, ChatMuteData mute)
		{
			DateTime now = DateTime.UtcNow;
			if (!mute.IsActiveAt(now))
			{
				return $"{scope}: not muted.";
			}

			string span = mute.MutedUntilUtc == null
				? "with no end"
				: "for " + OperatorCommandParsing.DescribeDuration(mute.MutedUntilUtc.Value - now);
			string by = string.IsNullOrEmpty(mute.MutedBy) ? string.Empty : " by " + mute.MutedBy;
			return $"{scope}: MUTED {span}{by}: {mute.Reason ?? "no reason recorded"}";
		}

		#endregion

		#region Movement

		/// <summary>Moves the caller to a character.</summary>
		private void GoToCharacter(IPlayerCharacter character, string arguments)
		{
			/* moves the OPERATOR, not the target, so it is not an escalation to run it on a peer. */
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target,
				StaffTargetRank.SkipOutranks))
			{
				return;
			}
			if (target.ID == character.ID)
			{
				Reply(character, "You are already there.");
				return;
			}
			if (!TryMove(character, character, target, out string failure))
			{
				Reply(character, failure);
				return;
			}
			Reply(character, $"Moved to {target.CharacterName}.");
		}

		/// <summary>Moves a character to the caller.</summary>
		/// <remarks>
		/// The summoned player is told, and told by whom. Being moved without explanation is
		/// indistinguishable from a bug or an exploit, and a player who cannot tell those apart
		/// files a report about the wrong thing.
		/// </remarks>
		private void SummonCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}
			if (target.ID == character.ID)
			{
				Reply(character, "You cannot summon yourself.");
				return;
			}

			bool hadReturnPoint = summonReturnPoints.ContainsKey(target.ID);
			if (!hadReturnPoint && target.GameObject != null && target.Transform != null)
			{
				PruneSummonReturnPoints();
				summonReturnPoints[target.ID] = new SummonReturnPoint()
				{
					SceneHandle = target.GameObject.scene.handle,
					Position = target.Transform.position,
					Rotation = target.Transform.rotation,
				};
			}

			if (!TryMove(character, target, character, out string failure))
			{
				if (!hadReturnPoint)
				{
					summonReturnPoints.Remove(target.ID);
				}
				Reply(character, failure);
				return;
			}

			Reply(character, $"Summoned {target.CharacterName}. /gm return {target.CharacterName} sends them back.");
			Reply(target, $"You have been summoned by {character.CharacterName}.");
		}

		/// <summary>Sends a summoned character back to where the first summon found them.</summary>
		private void ReturnSummonedCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}
			if (!summonReturnPoints.TryGetValue(target.ID, out SummonReturnPoint point))
			{
				Reply(character, $"No summon of {target.CharacterName} is recorded on this scene server.");
				return;
			}
			if (target.GameObject == null || target.GameObject.scene.handle != point.SceneHandle)
			{
				/* Same rule as TryMove: coordinates from one scene instance written into another put
				 * the player somewhere they are not loaded. The point is useless once they have left. */
				summonReturnPoints.Remove(target.ID);
				Reply(character, $"{target.CharacterName} has left the scene they were summoned from.");
				return;
			}
			if (!TryMoveTo(character, target, point.Position, point.Rotation, out string failure))
			{
				Reply(character, failure);
				return;
			}

			summonReturnPoints.Remove(target.ID);
			Reply(character, $"Returned {target.CharacterName}.");
			if (target.ID != character.ID)
			{
				Reply(target, $"{character.CharacterName} has sent you back.");
			}
		}

		/// <summary>Moves the caller to coordinates in their own scene.</summary>
		private void GoToPosition(IPlayerCharacter character, string arguments)
		{
			string xText = OperatorCommandParsing.SplitFirstWord(arguments, out string rest);
			string yText = OperatorCommandParsing.SplitFirstWord(rest, out rest);
			string zText = OperatorCommandParsing.SplitFirstWord(rest, out _);

			if (!TryParseCoordinate(xText, out float x) ||
				!TryParseCoordinate(yText, out float y) ||
				!TryParseCoordinate(zText, out float z))
			{
				ReplyUsage(character, gameMasterCommands, "gotopos");
				return;
			}

			Quaternion rotation = character.Transform != null ? character.Transform.rotation : Quaternion.identity;
			if (!TryMoveTo(character, character, new Vector3(x, y, z), rotation, out string failure))
			{
				Reply(character, failure);
				return;
			}
			Reply(character, $"Moved to {x.ToString("0.0", CultureInfo.InvariantCulture)}, {y.ToString("0.0", CultureInfo.InvariantCulture)}, {z.ToString("0.0", CultureInfo.InvariantCulture)}.");
		}

		/// <summary>Parses one finite, bounded coordinate.</summary>
		private static bool TryParseCoordinate(string text, out float value)
		{
			return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
				!float.IsNaN(value) && !float.IsInfinity(value) &&
				Mathf.Abs(value) <= MaxTeleportCoordinate;
		}

		/// <summary>Moves a character to the nearest respawn point in the scene they are standing in.</summary>
		/// <remarks>
		/// The scene they are physically in — the instance, when they are in one — because a respawn
		/// point from the open world written into a dungeon is exactly the fall-through-the-world
		/// placement this command exists to fix. <c>rescue</c> with no point named does the same.
		/// </remarks>
		private void UnstickCharacter(IPlayerCharacter character, string arguments)
		{
			if (!TryResolveTarget(character, arguments, out IPlayerCharacter target))
			{
				return;
			}
			RescueTo(character, target, RescuePointKind.Respawn, null);
		}

		/// <summary>
		/// <c>rescue &lt;character&gt; &lt;respawn|spawn|waypoint&gt; [point]</c> — moves a character to an
		/// authored place in the scene they are in.
		/// </summary>
		/// <remarks>
		/// The places come from the world scene details cache for the character's own scene, never the
		/// operator's: the operator may be standing somewhere else entirely, and only a point in the
		/// scene the character is loaded into is a place they can be put. <c>points</c> lists the names.
		/// </remarks>
		private void RescueCharacter(IPlayerCharacter character, string arguments)
		{
			string name = OperatorCommandParsing.SplitFirstWord(arguments, out string afterName);
			string kindWord = OperatorCommandParsing.SplitFirstWord(afterName, out string pointName);

			if (name.Length == 0 || !RescuePoints.TryParseKind(kindWord, out RescuePointKind kind))
			{
				ReplyUsage(character, gameMasterCommands, "rescue");
				return;
			}
			if (!TryResolveTarget(character, name, out IPlayerCharacter target))
			{
				return;
			}
			RescueTo(character, target, kind, pointName);
		}

		/// <summary>The shared body of <c>unstuck</c> and <c>rescue</c>.</summary>
		private void RescueTo(IPlayerCharacter character, IPlayerCharacter target, RescuePointKind kind, string pointName)
		{
			string scene = target.CurrentSceneName();
			string kindWord = RescuePoints.KindWords[(int)kind];

			if (!TryGetSceneDetails(scene, out WorldSceneDetails details) || target.Transform == null)
			{
				Reply(character, $"'{scene}' has no scene details to rescue {target.CharacterName} with.");
				return;
			}

			if (!RescuePoints.TryFind(details, kind, pointName, target.Transform.position, out RescuePoint point))
			{
				Reply(character, string.IsNullOrWhiteSpace(pointName)
					? $"'{scene}' has no {kindWord} point. See /gm points {target.CharacterName}."
					: $"'{scene}' has no {kindWord} called '{OperatorCommandParsing.Truncate(pointName.Trim(), 32)}'. See /gm points.");
				return;
			}

			// A waypoint is a map marker with no facing; keep the character's own.
			Quaternion rotation = point.HasRotation ? point.Rotation : target.Transform.rotation;
			if (!TryMoveTo(character, target, point.Position, rotation, out _))
			{
				Reply(character, $"{target.CharacterName} cannot be moved right now.");
				return;
			}

			Reply(character, $"Moved {target.CharacterName} to {kindWord} '{point.Name}' in '{scene}'.");
			if (target.ID != character.ID)
			{
				Reply(target, $"{character.CharacterName} has moved you to safety.");
			}
		}

		/// <summary>
		/// <c>points [character]</c> — lists the places <c>rescue</c> can use in a character's scene,
		/// the operator's own when none is named.
		/// </summary>
		private void ListRescuePoints(IPlayerCharacter character, string arguments)
		{
			// Lists the rescue points in the target's scene. Reads only the scene name; nothing changes.
			if (!TryResolveOptionalTarget(character, arguments, out IPlayerCharacter target, out _, StaffTargetRank.SkipOutranks))
			{
				return;
			}

			string scene = target.CurrentSceneName();
			if (!TryGetSceneDetails(scene, out WorldSceneDetails details))
			{
				Reply(character, $"'{scene}' has no scene details.");
				return;
			}

			List<string> lines = new List<string> { $"Rescue points in '{scene}':" };
			for (int i = 0; i < RescuePoints.KindWords.Length; ++i)
			{
				List<RescuePoint> points = RescuePoints.List(details, (RescuePointKind)i);
				List<string> names = new List<string>(points.Count);
				for (int j = 0; j < points.Count; ++j)
				{
					names.Add(points[j].Name);
				}
				if (names.Count < 1)
				{
					lines.Add($"{RescuePoints.KindWords[i]}: none");
					continue;
				}
				lines.AddRange(OperatorCommandParsing.PackLines(names, $"{RescuePoints.KindWords[i]}: ", ", ", ChatBroadcast.MaxTextLength));
			}
			ReplyLines(character, lines);
		}

		/// <summary>Reads a scene's cached details, when the cache has any.</summary>
		private bool TryGetSceneDetails(string scene, out WorldSceneDetails details)
		{
			details = null;
			return WorldSceneDetailsCache != null &&
				!string.IsNullOrEmpty(scene) &&
				WorldSceneDetailsCache.Scenes.TryGetValue(scene, out details) &&
				details != null;
		}

		/// <summary>
		/// Places <paramref name="moved"/> just behind <paramref name="destination"/>.
		/// </summary>
		/// <remarks>
		/// Same scene instance only. A scene server runs several scenes: writing one character's
		/// position into another's scene would put them at valid coordinates in a place they are not
		/// loaded into, which reads to the player as falling through the world. Compared by handle,
		/// because scene stacking means two instances can share a name.
		/// </remarks>
		private bool TryMove(IPlayerCharacter actor, IPlayerCharacter moved, IPlayerCharacter destination, out string failure)
		{
			if (moved.GameObject == null || destination.GameObject == null ||
				moved.GameObject.scene.handle != destination.GameObject.scene.handle)
			{
				failure = $"{moved.CharacterName} and {destination.CharacterName} are in different scenes.";
				return false;
			}
			if (destination.Transform == null)
			{
				failure = "That character cannot be moved right now.";
				return false;
			}

			// Slightly behind the destination rather than inside it: two capsules at identical
			// coordinates resolve by shoving each other apart, which looks like a bug.
			Vector3 offset = destination.Transform.forward * -1.5f;
			return TryMoveTo(actor, moved, destination.Transform.position + offset, destination.Transform.rotation, out failure);
		}

		/// <summary>
		/// Places a character at a position in the scene they are already in.
		/// </summary>
		/// <remarks>
		/// Moves through the motor rather than the transform. The motor is what the prediction
		/// system reconciles against; setting the transform directly is corrected away on the
		/// next tick, so the character snaps back and the command appears to have done nothing.
		/// </remarks>
		private bool TryMoveTo(IPlayerCharacter actor, IPlayerCharacter moved, Vector3 position, Quaternion rotation, out string failure)
		{
			if (moved.Motor == null)
			{
				failure = "That character cannot be moved right now.";
				return false;
			}

			moved.Motor.SetPositionAndRotationAndVelocity(position, rotation, Vector3.zero);

			Log.Warning("SceneServerSystem",
				$"'{actor.CharacterName}' ({actor.Account}) moved '{moved.CharacterName}' to {position} in '{moved.CurrentSceneName()}'.");

			failure = null;
			return true;
		}

		/// <summary>Drops return points for characters no longer on this scene server, once there are many.</summary>
		private void PruneSummonReturnPoints()
		{
			if (summonReturnPoints.Count < MaxSummonReturnPoints || !TryGetOnlineCharacters(out var mapping))
			{
				return;
			}
			foreach (long id in summonReturnPoints.Keys.Where(id => !mapping.CharactersByID.ContainsKey(id)).ToList())
			{
				summonReturnPoints.Remove(id);
			}
		}

		#endregion
	}
}
