using FishNet.Connection;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishMMO.Auth.Core;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using Channel = FishNet.Transporting.Channel;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The server half of the in-game staff console: the catalogue that populates it, and the
	/// read requests behind its roster and ticket views.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The client ships an empty console. It is filled only by the catalogue sent here, only to an
	/// account at GameMaster or above, and only in answer to <c>/gm console</c>. See
	/// <see cref="StaffConsoleCatalogBroadcast"/> for the whole model.
	/// </para>
	/// <para>
	/// <b>Every read request is re-authorised on arrival</b>, against the character this server
	/// loaded for the connection. A request from an account below GameMaster is answered with
	/// nothing and reported through <see cref="ChatHelper.ReportRefused"/>, which logs and audits it
	/// exactly as a refused command. A request that is allowed is not audited, matching the Control
	/// Panel's reads; every ACTION the console takes arrives as a chat command and is audited at the
	/// gate.
	/// </para>
	/// </remarks>
	public partial class SceneServerSystem
	{
		/// <summary>Least time between two requests of one kind from one staff member.</summary>
		/// <remarks>
		/// A roster built every frame by a misbehaving client is a scene server doing needless work
		/// for one connection; the console refreshes every few seconds and never needs faster.
		/// </remarks>
		private static readonly long StaffRequestIntervalTicks = TimeSpan.FromMilliseconds(500).Ticks;

		/// <summary>Longest message body a ticket detail carries.</summary>
		private const int StaffTicketTextLength = 1024;

		/// <summary>When each staff member may next make each kind of request, by (character, kind).</summary>
		private readonly Dictionary<long, long> staffRequestNextTicks = new Dictionary<long, long>();

		/// <summary>Registers the staff console's read requests.</summary>
		private void RegisterStaffConsoleBroadcasts()
		{
			Server.NetworkWrapper.RegisterBroadcast<StaffRosterRequestBroadcast>(OnStaffRosterRequest, true);
			Server.NetworkWrapper.RegisterBroadcast<StaffTicketQueueRequestBroadcast>(OnStaffTicketQueueRequest, true);
			Server.NetworkWrapper.RegisterBroadcast<StaffTicketDetailRequestBroadcast>(OnStaffTicketDetailRequest, true);
		}

		/// <summary>Unregisters the staff console's read requests.</summary>
		private void UnregisterStaffConsoleBroadcasts()
		{
			if (Server?.NetworkWrapper != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<StaffRosterRequestBroadcast>(OnStaffRosterRequest);
				Server.NetworkWrapper.UnregisterBroadcast<StaffTicketQueueRequestBroadcast>(OnStaffTicketQueueRequest);
				Server.NetworkWrapper.UnregisterBroadcast<StaffTicketDetailRequestBroadcast>(OnStaffTicketDetailRequest);
			}
			staffRequestNextTicks.Clear();
		}

		/// <summary>
		/// Sends the console catalogue: every command this account may run, and the views it may use.
		/// </summary>
		/// <remarks>
		/// Reached only through <c>/gm console</c>, so the access gate has already allowed and audited
		/// it; the level test here is a second statement of the same rule, so that a future caller of
		/// this method cannot hand the catalogue to a player by mistake.
		/// </remarks>
		private void SendStaffConsoleCatalog(IPlayerCharacter character, bool open)
		{
			NetworkConnection conn = character?.Owner;
			if (conn == null || !conn.IsActive || character.AccessLevel < AccessLevel.GameMaster)
			{
				return;
			}

			var entries = new List<StaffCommandEntry>();
			foreach (OperatorCommandSet set in new[] { gameMasterCommands, adminCommands })
			{
				if (set == null || character.AccessLevel < set.Level)
				{
					continue;
				}
				foreach (OperatorCommand command in set.Commands)
				{
					if (command.Name == "console")
					{
						continue;
					}
					entries.Add(new StaffCommandEntry()
					{
						Command = set.Command,
						Name = command.Name,
						Category = command.Category ?? string.Empty,
						Summary = command.Summary ?? string.Empty,
						Arguments = command.Arguments ?? string.Empty,
						RosterAction = command.RosterAction,
						TicketAction = command.TicketAction,
						Destructive = command.Destructive,
					});
				}
			}

			Server.NetworkWrapper.Broadcast(conn, new StaffConsoleCatalogBroadcast()
			{
				AccessLevel = (byte)character.AccessLevel,
				Open = open,
				Views = new[] { "players", "tickets" },
				Commands = entries.ToArray(),
			}, true, Channel.Reliable);

			Reply(character, $"Staff console opened with {entries.Count} command(s).");
		}

		/// <summary>
		/// Resolves the staff member behind a console request and decides whether to answer it.
		/// </summary>
		/// <param name="conn">The connection the request arrived on.</param>
		/// <param name="request">A stable name for the request, recorded on refusal.</param>
		/// <param name="kind">Which request, for the rate limit.</param>
		/// <param name="character">The authorised staff member.</param>
		/// <returns>True when the request should be answered.</returns>
		private bool TryAuthorizeStaffRequest(NetworkConnection conn, string request, int kind, out IPlayerCharacter character)
		{
			character = null;
			if (conn == null ||
				!TryGetOnlineCharacters(out var mapping) ||
				!mapping.ConnectionCharacters.TryGetValue(conn, out character) ||
				character == null)
			{
				return false;
			}

			/* Banned is zero, so the explicit test is not redundant with the minimum: nothing is
			 * answered for a banned character, whatever the minimum becomes. */
			if (character.AccessLevel <= AccessLevel.Banned || character.AccessLevel < AccessLevel.GameMaster)
			{
				ChatHelper.ReportRefused(character, "staffconsole." + request, AccessLevel.GameMaster);
				return false;
			}

			long now = DateTime.UtcNow.Ticks;
			long key = character.ID * 4 + kind;
			if (staffRequestNextTicks.TryGetValue(key, out long next) && next > now)
			{
				return false;
			}
			if (staffRequestNextTicks.Count > 1024)
			{
				staffRequestNextTicks.Clear();
			}
			staffRequestNextTicks[key] = now + StaffRequestIntervalTicks;
			return true;
		}

		/// <summary>Answers a roster request with every character in the requester's scene instance.</summary>
		private void OnStaffRosterRequest(NetworkConnection conn, StaffRosterRequestBroadcast msg, Channel channel)
		{
			if (!TryAuthorizeStaffRequest(conn, "roster", 0, out IPlayerCharacter staff) ||
				!TryGetOnlineCharacters(out var mapping) ||
				staff.GameObject == null)
			{
				return;
			}

			/* By scene handle, not scene name: this server hosts several scenes and scene stacking
			 * means several instances of one. The roster answers "who is here with me". */
			int handle = staff.GameObject.scene.handle;
			Vector3 origin = staff.Transform != null ? staff.Transform.position : Vector3.zero;
			long now = DateTime.UtcNow.Ticks;

			var rows = new List<StaffRosterEntry>();
			foreach (IPlayerCharacter character in mapping.CharactersByID.Values)
			{
				if (character?.GameObject == null || character.GameObject.scene.handle != handle)
				{
					continue;
				}
				rows.Add(new StaffRosterEntry()
				{
					CharacterID = character.ID,
					Name = character.CharacterName ?? string.Empty,
					Account = character.Account ?? string.Empty,
					AccessLevel = (byte)character.AccessLevel,
					Dead = character.IsFlagged(CharacterFlags.IsDead),
					InCombat = character.IsFlagged(CharacterFlags.IsInCombat),
					Muted = character.ChatMutedUntilTicks > now,
					Distance = character.Transform != null ? Vector3.Distance(origin, character.Transform.position) : 0f,
				});
			}

			int total = rows.Count;
			rows.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			if (rows.Count > StaffRosterBroadcast.MaxEntries)
			{
				rows.RemoveRange(StaffRosterBroadcast.MaxEntries, rows.Count - StaffRosterBroadcast.MaxEntries);
			}

			Server.NetworkWrapper.Broadcast(conn, new StaffRosterBroadcast()
			{
				SceneName = staff.CurrentSceneName() ?? string.Empty,
				TotalInScene = total,
				TotalOnServer = mapping.CharactersByID.Count,
				Characters = rows.ToArray(),
			}, true, Channel.Reliable);
		}

		/// <summary>Answers a ticket queue request with one page.</summary>
		private void OnStaffTicketQueueRequest(NetworkConnection conn, StaffTicketQueueRequestBroadcast msg, Channel channel)
		{
			if (!TryAuthorizeStaffRequest(conn, "tickets", 1, out IPlayerCharacter staff))
			{
				return;
			}

			StaffTicketFilter filter = Enum.IsDefined(typeof(StaffTicketFilter), msg.Filter) ? msg.Filter : StaffTicketFilter.Unassigned;
			int page = Mathf.Clamp(msg.Page, 1, 10_000);
			string account = staff.Account;
			long staffID = staff.ID;

			TryEnqueueAsyncWork(async () =>
			{
				var response = new StaffTicketQueueBroadcast()
				{
					Filter = filter,
					Page = page,
					Tickets = Array.Empty<StaffTicketSummary>(),
				};
				string failure = null;

				try
				{
					if (!TryGetDbService(out ISupportTicketService tickets))
					{
						failure = "Support tickets are unavailable.";
					}
					else
					{
						DatabaseResult<SupportTicketPage> result = await tickets.SearchAsync(
							BuildStaffTicketQuery(filter, account, page, StaffTicketQueueBroadcast.PageSize));
						if (!result.IsSuccess || result.Data == null)
						{
							failure = "The ticket queue could not be read.";
						}
						else
						{
							response.TotalCount = result.Data.TotalCount;
							response.Tickets = (result.Data.Items ?? Array.Empty<SupportTicketData>())
								.Where(t => t != null)
								.Select(ToStaffTicketSummary)
								.ToArray();
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("SceneServerSystem", $"Staff ticket queue failed for '{account}': {ex}");
					failure = "The ticket queue could not be read.";
				}

				TryEnqueueMainThread(() => AnswerStaffRequest(staffID, failure, response));
			}, staffID);
		}

		/// <summary>Answers a ticket detail request, internal notes included.</summary>
		private void OnStaffTicketDetailRequest(NetworkConnection conn, StaffTicketDetailRequestBroadcast msg, Channel channel)
		{
			if (!TryAuthorizeStaffRequest(conn, "ticket", 2, out IPlayerCharacter staff) || msg.TicketID <= 0)
			{
				return;
			}

			long ticketID = msg.TicketID;
			string account = staff.Account;
			long staffID = staff.ID;

			TryEnqueueAsyncWork(async () =>
			{
				var response = new StaffTicketDetailBroadcast()
				{
					Found = false,
					Ticket = new StaffTicketSummary() { TicketID = ticketID },
					Messages = Array.Empty<StaffTicketMessageEntry>(),
				};
				string failure = null;

				try
				{
					if (!TryGetDbService(out ISupportTicketService tickets))
					{
						failure = "Support tickets are unavailable.";
					}
					else
					{
						DatabaseResult<SupportTicketData> result = await tickets.FetchAsync(ticketID, includeInternal: true);
						if (result.IsSuccess && result.Data != null)
						{
							SupportTicketData ticket = result.Data;
							var messages = (ticket.Messages ?? Array.Empty<SupportTicketMessageData>())
								.Where(m => m != null)
								.OrderBy(m => m.CreatedUtc)
								.ToList();

							response.Found = true;
							response.Ticket = ToStaffTicketSummary(ticket);
							response.Body = OperatorCommandParsing.Truncate(ticket.Body ?? string.Empty, StaffTicketTextLength);
							response.SceneName = ticket.SceneName ?? string.Empty;
							response.Resolution = ticket.Resolution ?? string.Empty;
							response.Messages = messages
								.Skip(Math.Max(0, messages.Count - StaffTicketDetailBroadcast.MaxMessages))
								.Select(m => new StaffTicketMessageEntry()
								{
									CreatedUtcTicks = m.CreatedUtc.Ticks,
									Author = m.AuthorAccount ?? string.Empty,
									AuthorIsStaff = m.AuthorIsStaff,
									Internal = m.Internal,
									Body = OperatorCommandParsing.Truncate(m.Body ?? string.Empty, StaffTicketTextLength),
								})
								.ToArray();
						}
					}
				}
				catch (Exception ex)
				{
					await Log.Error("SceneServerSystem", $"Staff ticket detail {ticketID} failed for '{account}': {ex}");
					failure = $"Ticket #{ticketID} could not be read.";
				}

				TryEnqueueMainThread(() => AnswerStaffRequest(staffID, failure, response));
			}, staffID);
		}

		/// <summary>
		/// Delivers a staff read's answer on the main thread, to the staff member if they are still here.
		/// </summary>
		/// <remarks>
		/// Resolved by id, and the level re-checked: an account demoted while the read ran is not
		/// sent what it asked for as a game master.
		/// </remarks>
		private void AnswerStaffRequest<T>(long staffID, string failure, T response) where T : struct, FishNet.Broadcast.IBroadcast
		{
			if (!TryGetOnlineCharacters(out var mapping) ||
				!mapping.CharactersByID.TryGetValue(staffID, out IPlayerCharacter staff) ||
				staff?.Owner == null ||
				!staff.Owner.IsActive ||
				staff.AccessLevel < AccessLevel.GameMaster)
			{
				return;
			}

			if (failure != null)
			{
				Reply(staff, failure);
				return;
			}
			Server.NetworkWrapper.Broadcast(staff.Owner, response, true, Channel.Reliable);
		}

		/// <summary>Projects a ticket onto the console's summary shape.</summary>
		private static StaffTicketSummary ToStaffTicketSummary(SupportTicketData ticket)
		{
			return new StaffTicketSummary()
			{
				TicketID = ticket.ID,
				Status = ticket.Status.ToString(),
				Category = ticket.Category.ToString(),
				Priority = ticket.Priority,
				Subject = ticket.Subject ?? string.Empty,
				ReporterAccount = ticket.ReporterAccount ?? string.Empty,
				ReporterCharacter = ticket.ReporterCharacterName ?? string.Empty,
				TargetCharacter = ticket.TargetCharacterName ?? string.Empty,
				AssignedTo = ticket.AssignedTo ?? string.Empty,
				LastActivityUtcTicks = ticket.LastActivityUtc.Ticks,
			};
		}
	}
}
