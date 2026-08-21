using FishNet.Connection;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Guild recruitment (E10): the advertisement, the public directory, and the application queue.
	/// </summary>
	/// <remarks>
	/// The interesting problem here is not the queue, it is the RACES. An application sits around
	/// for as long as it takes an officer to look at it, and in that window the guild can fill up,
	/// disband, or stop recruiting; the applicant can join somewhere else, block the recruiter, or
	/// apply again from a second client. Each of those is handled where it can actually be
	/// decided, which is mostly not in this file — see <see cref="ResolveGuildApplicationAsync"/>.
	/// </remarks>
	public partial class GuildSystem
	{
		/// <summary>
		/// Handles a request to change the guild's recruitment advertisement.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The proposed blurb, tags and recruiting flag.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildSetRecruitmentBroadcastReceived(NetworkConnection conn, GuildSetRecruitmentBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.SetRecruitment, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (!guildController.HasGuildPermission(GuildPermissions.EditRecruitment))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				/* Sanitised at the boundary. The blurb is shown to STRANGERS, in a list, next to
				 * other guilds' blurbs — it is the one piece of guild text a player sees without
				 * having chosen to associate with its author, so leaving markup or control
				 * characters in it would let one guild's advertisement wreck the rendering of
				 * everybody else's. */
				string blurb = ChatSanitizer.SanitizeIncoming(msg.Blurb, GuildTextLimits.MaxBlurbLength);
				string tags = NormalizeTags(msg.Tags);
				bool isRecruiting = msg.IsRecruiting;

				long guildID = guildController.ID;
				long editorCharacterID = guildController.Character.ID;

				deferGuardRelease = TryEnqueueIngressWork(
					() => SetGuildRecruitmentAsync(conn, guildID, editorCharacterID, blurb, tags, isRecruiting),
					guardKey,
					guildID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Persists the recruitment advertisement and republishes it to the guild.
		/// </summary>
		/// <param name="conn">The requesting connection, for feedback.</param>
		/// <param name="guildID">The guild.</param>
		/// <param name="editorCharacterID">The editing character.</param>
		/// <param name="blurb">The sanitised advertisement.</param>
		/// <param name="tags">The normalised tag list.</param>
		/// <param name="isRecruiting">Whether the guild should be listed.</param>
		/// <returns>Asynchronous write task.</returns>
		private async Task SetGuildRecruitmentAsync(NetworkConnection conn, long guildID, long editorCharacterID, string blurb, string tags, bool isRecruiting)
		{
			try
			{
				if (!TryGetDbService(out IGuildService guildService))
				{
					return;
				}

				GuildAuthority editor = await ResolveGuildAuthorityAsync(guildID, editorCharacterID);
				if (!editor.Has(GuildPermissions.EditRecruitment))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				DatabaseResult persistResult = await guildService.PersistRecruitmentAsync(guildID, blurb, tags, isRecruiting);
				if (!persistResult.IsSuccess)
				{
					await Log.Warning("GuildSystem", $"SetGuildRecruitmentAsync persist failed (GuildID={guildID}): {persistResult.ErrorCode} - {persistResult.ErrorMessage}");
					return;
				}

				AppendGuildLog(guildID, GuildLogEventType.RecruitmentChanged, editorCharacterID);

				await PublishGuildRecruitmentInfoAsync(guildID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error setting guild recruitment (GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Sends a guild's own advertisement to its members on this server.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="onlyCharacterID">Optional single recipient.</param>
		/// <returns>Asynchronous publish task.</returns>
		private async Task PublishGuildRecruitmentInfoAsync(long guildID, long onlyCharacterID = 0)
		{
			if (!TryGetDbService(out IGuildService guildService))
			{
				return;
			}

			DatabaseResult<GuildData?> guildResult = await guildService.FetchAsync(guildID);
			if (!guildResult.IsSuccess || !guildResult.Data.HasValue)
			{
				return;
			}

			GuildData guild = guildResult.Data.Value;

			GuildRecruitmentInfoBroadcast broadcast = new GuildRecruitmentInfoBroadcast()
			{
				GuildID = guild.ID,
				Blurb = guild.Blurb ?? string.Empty,
				Tags = guild.Tags ?? string.Empty,
				IsRecruiting = guild.IsRecruiting,
			};

			BroadcastToGuildMembers(guildID, broadcast, onlyCharacterID);
		}

		/// <summary>
		/// Handles a request for a page of the recruitment directory.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The optional search term.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		/// <remarks>
		/// The ONE guild request a non-member may make, so it does not go through
		/// <c>TryBeginGuildRequest</c> — which requires a guild — and instead does the
		/// connection checks itself. Browsing is deliberately open to members too: a player
		/// shopping for a new guild has not left their old one yet.
		/// </remarks>
		public void OnServerGuildDirectoryRequestBroadcastReceived(NetworkConnection conn, GuildDirectoryRequestBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Directory, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				string searchTerm = ChatSanitizer.SanitizeIncoming(msg.SearchTerm, GuildTextLimits.MaxDirectorySearchLength);

				deferGuardRelease = TryEnqueueIngressWork(
					() => SendGuildDirectoryAsync(conn, searchTerm),
					guardKey,
					player.ID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Reads the directory and sends one page to a connection.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="searchTerm">The sanitised search term.</param>
		/// <returns>Asynchronous send task.</returns>
		private async Task SendGuildDirectoryAsync(NetworkConnection conn, string searchTerm)
		{
			try
			{
				if (!TryGetDbService(out IGuildApplicationService applicationService))
				{
					return;
				}

				DatabaseResult<IReadOnlyList<GuildDirectoryEntryData>> searchResult =
					await applicationService.SearchDirectoryAsync(searchTerm, guildDirectoryPageSize);

				if (!searchResult.IsSuccess || searchResult.Data == null)
				{
					return;
				}

				IReadOnlyList<GuildDirectoryEntryData> rows = searchResult.Data;
				GuildDirectoryEntry[] entries = new GuildDirectoryEntry[rows.Count];
				for (int i = 0; i < rows.Count; ++i)
				{
					GuildDirectoryEntryData row = rows[i];
					entries[i] = new GuildDirectoryEntry()
					{
						GuildID = row.ID,
						Name = row.Name ?? string.Empty,
						Blurb = row.Blurb ?? string.Empty,
						Tags = row.Tags ?? string.Empty,
						MemberCount = row.MemberCount,
						/* Sent rather than assumed. The cap is a server setting the client has no
						 * copy of, and a directory that rendered "37 members" without saying out
						 * of how many tells the reader nothing about whether they can get in. */
						MaxMemberCount = maxGuildSize,
					};
				}

				GuildDirectoryBroadcast broadcast = new GuildDirectoryBroadcast()
				{
					Entries = entries,
				};

				TryEnqueueMainThread(() =>
				{
					if (conn != null && conn.IsActive && Server != null)
					{
						Server.NetworkWrapper.Broadcast(conn, broadcast, true, Channel.Reliable);
					}
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error sending guild directory: {ex}");
			}
		}

		/// <summary>
		/// Handles a request to apply to a guild found in the directory.
		/// </summary>
		/// <param name="conn">The applying connection.</param>
		/// <param name="msg">The guild and the applicant's message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		/// <remarks>
		/// Refused for a character who is already in a guild — checked here on the cheap side, and
		/// again in the SQL, because the controller's guild ID is main-thread state and the
		/// membership row is the truth.
		/// </remarks>
		public void OnServerGuildApplyBroadcastReceived(NetworkConnection conn, GuildApplyBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter player = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (player == null || !CharacterStateValidation.CanAct(player))
			{
				return;
			}

			if (Server?.Database?.ServiceRegistry == null || msg.GuildID < 1)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Apply, out long guardKey))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
				if (guildController == null || guildController.ID > 0)
				{
					// Already in a guild. Leave first.
					SendGuildResult(conn, GuildResultType.AlreadyInGuild);
					return;
				}

				/* The application spam guard. The ingress debounce is per connection and per
				 * operation, which stops a held-down button but not a script pacing itself just
				 * over the debounce; the one-pending-per-guild unique index stops repeats to the
				 * SAME guild but not a sweep across every guild in the directory. This is the one
				 * that stops the sweep. */
				if (!TryBeginApplicationCooldown(player.ID))
				{
					SendGuildResult(conn, GuildResultType.ApplyOnCooldown);
					return;
				}

				string message = ChatSanitizer.SanitizeIncoming(msg.Message, GuildTextLimits.MaxApplicationMessageLength);

				long characterID = player.ID;
				long guildID = msg.GuildID;

				deferGuardRelease = TryEnqueueIngressWork(
					() => ApplyToGuildAsync(conn, characterID, guildID, message),
					guardKey,
					characterID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Submits one application.
		/// </summary>
		/// <param name="conn">The applying connection, for feedback.</param>
		/// <param name="characterID">The applying character.</param>
		/// <param name="guildID">The guild applied to.</param>
		/// <param name="message">The sanitised applicant message.</param>
		/// <returns>Asynchronous apply task.</returns>
		/// <remarks>
		/// <para>
		/// Almost nothing is decided here. Whether the guild exists, is recruiting, has room, and
		/// whether this character already has an application outstanding are ALL decided inside
		/// the INSERT — see <c>IGuildApplicationService.ApplyAsync</c>. Testing them here and
		/// inserting afterwards is a gap the applicant controls the timing of.
		/// </para>
		/// <para>
		/// What is decided here is blocking, because it is a fact about two characters rather than
		/// about the guild row. Asked in the direction "has anybody in a position to see this
		/// application blocked the applicant" — the guild's leader stands in for the guild, since
		/// there is no such thing as blocking a guild.
		/// </para>
		/// </remarks>
		private async Task ApplyToGuildAsync(NetworkConnection conn, long characterID, long guildID, string message)
		{
			try
			{
				if (!TryGetDbService(out IGuildApplicationService applicationService) ||
					!TryGetDbService(out ICharacterGuildService charGuildService))
				{
					return;
				}

				/* Re-established against the database, not the controller: the controller's guild
				 * ID is main-thread state that a join landing on another scene server has not
				 * reached yet. */
				DatabaseResult<CharacterGuildData?> existingResult = await charGuildService.FetchAsync(characterID);
				if (existingResult.IsSuccess && existingResult.Data.HasValue)
				{
					SendGuildResult(conn, GuildResultType.AlreadyInGuild);
					return;
				}

				if (await IsBlockedByGuildLeadershipAsync(guildID, characterID))
				{
					/* Reported as an ordinary refusal, deliberately. Telling the applicant they
					 * were blocked would turn the block list into an oracle. */
					SendGuildResult(conn, GuildResultType.NotRecruiting);
					return;
				}

				DatabaseResult applyResult = await applicationService.ApplyAsync(
					guildID,
					characterID,
					message,
					maxGuildSize,
					maxPendingApplicationsPerCharacter);

				if (!applyResult.IsSuccess)
				{
					SendGuildResult(conn, applyResult.ErrorCode == DatabaseErrorCodes.UniqueViolation
						? GuildResultType.AlreadyApplied
						: GuildResultType.NotRecruiting);
					return;
				}

				SendGuildResult(conn, GuildResultType.ApplicationSent);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error applying to guild (CharID={characterID}, GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Handles a request for the guild's pending application queue.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The request. Carries nothing.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildApplicationListRequestBroadcastReceived(NetworkConnection conn, GuildApplicationListRequestBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.ApplicationList, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (!guildController.HasGuildPermission(GuildPermissions.ManageApplications))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				long guildID = guildController.ID;
				long characterID = guildController.Character.ID;

				deferGuardRelease = TryEnqueueIngressWork(
					() => SendGuildApplicationsAsync(conn, guildID, characterID),
					guardKey,
					guildID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Reads the pending queue and sends it to one connection.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="guildID">The guild.</param>
		/// <param name="characterID">The requesting character.</param>
		/// <returns>Asynchronous send task.</returns>
		/// <remarks>
		/// The permission is re-resolved before the queue is READ, not only before it is acted on.
		/// The queue is a list of who wants to join, which is guild business rather than public
		/// information, and a demoted officer should stop being able to see it immediately.
		/// </remarks>
		private async Task SendGuildApplicationsAsync(NetworkConnection conn, long guildID, long characterID)
		{
			try
			{
				if (!TryGetDbService(out IGuildApplicationService applicationService))
				{
					return;
				}

				GuildAuthority authority = await ResolveGuildAuthorityAsync(guildID, characterID);
				if (!authority.Has(GuildPermissions.ManageApplications))
				{
					return;
				}

				DatabaseResult<IReadOnlyList<GuildApplicationData>> fetchResult =
					await applicationService.FetchManyAsync(guildID, guildApplicationPageSize);

				if (!fetchResult.IsSuccess || fetchResult.Data == null)
				{
					return;
				}

				IReadOnlyList<GuildApplicationData> rows = fetchResult.Data;
				GuildApplicationEntry[] entries = new GuildApplicationEntry[rows.Count];
				for (int i = 0; i < rows.Count; ++i)
				{
					GuildApplicationData row = rows[i];
					entries[i] = new GuildApplicationEntry()
					{
						ApplicationID = row.ID,
						CharacterID = row.CharacterID,
						Message = row.Message ?? string.Empty,
						TimeUtcTicks = row.TimeCreated.Ticks,
					};
				}

				GuildApplicationListBroadcast broadcast = new GuildApplicationListBroadcast()
				{
					GuildID = guildID,
					Entries = entries,
				};

				TryEnqueueMainThread(() =>
				{
					if (conn == null || !conn.IsActive || Server == null || conn.FirstObject == null)
					{
						return;
					}

					// Re-checked on delivery: the requester may have left while the read was out.
					IGuildController guildController = conn.FirstObject.GetComponent<IGuildController>();
					if (guildController == null || guildController.ID != guildID)
					{
						return;
					}

					Server.NetworkWrapper.Broadcast(conn, broadcast, true, Channel.Reliable);
				});
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error sending guild applications (GuildID={guildID}): {ex}");
			}
		}

		/// <summary>
		/// Handles an accept or decline of one pending application.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="msg">The application and the decision.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerGuildResolveApplicationBroadcastReceived(NetworkConnection conn, GuildResolveApplicationBroadcast msg, Channel channel)
		{
			if (!TryBeginGuildRequest(conn, IngressOperation.ResolveApplication, out long guardKey, out IGuildController guildController))
			{
				return;
			}

			bool deferGuardRelease = false;
			try
			{
				if (!guildController.HasGuildPermission(GuildPermissions.ManageApplications))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				if (msg.ApplicationID < 1)
				{
					return;
				}

				long guildID = guildController.ID;
				long characterID = guildController.Character.ID;
				long applicationID = msg.ApplicationID;
				bool accept = msg.Accept;

				deferGuardRelease = TryEnqueueIngressWork(
					() => ResolveGuildApplicationAsync(conn, guildID, characterID, applicationID, accept),
					guardKey,
					guildID);

				if (!deferGuardRelease) SendServerBusy(conn);
			}
			finally
			{
				if (!deferGuardRelease)
				{
					EndIngressGuard(guardKey);
				}
			}
		}

		/// <summary>
		/// Accepts or declines one application.
		/// </summary>
		/// <param name="conn">The requesting connection, for feedback.</param>
		/// <param name="guildID">The guild.</param>
		/// <param name="resolverCharacterID">The officer resolving it.</param>
		/// <param name="applicationID">The application.</param>
		/// <param name="accept">True to admit the applicant.</param>
		/// <returns>Asynchronous resolve task.</returns>
		/// <remarks>
		/// <para>
		/// The ORDER matters, and it is the whole reason this method is careful. The application
		/// row is claimed FIRST, by a delete whose <c>WHERE</c> carries both the application ID
		/// and the guild ID and whose result says whether this call is the one that removed it.
		/// Two officers pressing Accept at the same instant therefore produce exactly one
		/// admission, because only one of them gets <c>true</c> back.
		/// </para>
		/// <para>
		/// Only then is the applicant admitted, through <c>JoinGuildAsync</c> — the same path an
		/// invitation takes. That is what re-checks, at admission time rather than at application
		/// time, that the guild still exists and still has room. An accept arriving after the
		/// guild filled fails there, as a refusal, rather than pushing the guild over its cap.
		/// </para>
		/// <para>
		/// Claiming the row before admitting means a failed admission consumes the application.
		/// That is the right way round: the alternative leaves a row that a second officer can
		/// accept again, and the applicant can simply re-apply.
		/// </para>
		/// </remarks>
		private async Task ResolveGuildApplicationAsync(NetworkConnection conn, long guildID, long resolverCharacterID, long applicationID, bool accept)
		{
			try
			{
				if (!TryGetDbService(out IGuildApplicationService applicationService) ||
					!TryGetDbService(out ICharacterGuildService charGuildService))
				{
					return;
				}

				GuildAuthority resolver = await ResolveGuildAuthorityAsync(guildID, resolverCharacterID);
				if (!resolver.Has(GuildPermissions.ManageApplications))
				{
					SendGuildResult(conn, GuildResultType.InsufficientRank);
					return;
				}

				DatabaseResult<GuildApplicationData?> fetchResult = await applicationService.FetchAsync(applicationID);
				if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
				{
					SendGuildResult(conn, GuildResultType.ApplicationNotFound);
					return;
				}

				GuildApplicationData application = fetchResult.Data.Value;
				if (application.GuildID != guildID)
				{
					// Another guild's application. The delete below would miss anyway; refuse here.
					SendGuildResult(conn, GuildResultType.ApplicationNotFound);
					return;
				}

				long applicantID = application.CharacterID;

				// Claim the row. Exactly one caller gets true.
				DatabaseResult<bool> claimResult = await applicationService.DeleteAsync(applicationID, guildID);
				if (!claimResult.IsSuccess || !claimResult.Data)
				{
					SendGuildResult(conn, GuildResultType.ApplicationNotFound);
					return;
				}

				if (!accept)
				{
					AppendGuildLog(guildID, GuildLogEventType.ApplicationDeclined, resolverCharacterID, applicantID);
					return;
				}

				/* The applicant may have joined somewhere else since applying. JoinGuildAsync
				 * would persist a second membership row for them; the membership table is keyed
				 * per character so it would either fail or overwrite, and overwriting would quietly
				 * move somebody out of a guild they chose and into one they had forgotten about. */
				DatabaseResult<CharacterGuildData?> applicantResult = await charGuildService.FetchAsync(applicantID);
				if (applicantResult.IsSuccess && applicantResult.Data.HasValue)
				{
					SendGuildResult(conn, GuildResultType.AlreadyInGuild);
					return;
				}

				AppendGuildLog(guildID, GuildLogEventType.ApplicationAccepted, resolverCharacterID, applicantID);

				await AdmitApplicantAsync(guildID, applicantID);
			}
			catch (Exception ex)
			{
				await Log.Error("GuildSystem", $"Error resolving guild application (GuildID={guildID}, ApplicationID={applicationID}): {ex}");
			}
		}

		/// <summary>
		/// Admits an accepted applicant through the ordinary join path.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="applicantID">The applicant.</param>
		/// <returns>Asynchronous admission task.</returns>
		/// <remarks>
		/// The applicant may be OFFLINE, or online on a different scene server. Their connection
		/// is looked up on the main thread and passed to <c>JoinGuildAsync</c> if it is here; if
		/// it is not, the join still happens — <c>JoinGuildAsync</c> tolerates a null connection,
		/// its main-thread block simply does nothing — and the applicant's own scene server picks
		/// the membership row up on its next guild-update pump. Requiring the applicant to be
		/// logged in and in the right zone at the moment an officer clicks Accept would make the
		/// queue nearly useless.
		/// </remarks>
		private async Task AdmitApplicantAsync(long guildID, long applicantID)
		{
			NetworkConnection applicantConn = null;
			string sceneName = string.Empty;

			TaskCompletionSource<bool> located = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			bool enqueued = TryEnqueueMainThread(() =>
			{
				try
				{
					if (Server != null &&
						Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData) &&
						characterMappingData.CharactersByID.TryGetValue(applicantID, out IPlayerCharacter applicant) &&
						applicant != null)
					{
						applicantConn = applicant.Owner;
						sceneName = applicant.SceneName ?? string.Empty;
					}
				}
				finally
				{
					located.TrySetResult(true);
				}
			});

			if (enqueued)
			{
				await located.Task;
			}

			if (string.IsNullOrEmpty(sceneName))
			{
				/* Offline, or on another server. "Offline" is the same label the disconnect
				 * persist writes, so the roster renders them as offline rather than as standing
				 * in a zone called "". */
				sceneName = "Offline";
			}

			await JoinGuildAsync(applicantConn, applicantID, guildID, sceneName, fromInvitation: false);
		}

		/// <summary>
		/// Whether the guild's most senior member has blocked the applicant, or vice versa.
		/// </summary>
		/// <param name="guildID">The guild.</param>
		/// <param name="applicantID">The applicant.</param>
		/// <returns>True when the application should be quietly refused.</returns>
		/// <remarks>
		/// There is no such thing as blocking a guild, so the guild's top-ranked member stands in
		/// for it — they are the person who would have to deal with the applicant. Checked in BOTH
		/// directions: a player who blocked a guild leader should not have to see that guild's
		/// invitations, and a leader who blocked a player should not have to see their
		/// applications.
		/// </remarks>
		private async Task<bool> IsBlockedByGuildLeadershipAsync(long guildID, long applicantID)
		{
			if (!TryGetDbService(out ICharacterFriendService friendService) ||
				!TryGetDbService(out ICharacterGuildService charGuildService))
			{
				return false;
			}

			DatabaseResult<IReadOnlyList<CharacterGuildData>> membersResult = await charGuildService.FetchManyAsync(guildID);
			if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count == 0)
			{
				return false;
			}

			long leaderID = 0;
			byte topRankOrder = 0;
			foreach (CharacterGuildData member in membersResult.Data)
			{
				if (member.Rank > topRankOrder)
				{
					topRankOrder = member.Rank;
					leaderID = member.CharacterID;
				}
			}

			if (leaderID < 1)
			{
				return false;
			}

			DatabaseResult<bool> leaderBlockedApplicant = await friendService.IsBlockedAsync(leaderID, applicantID);
			if (leaderBlockedApplicant.IsSuccess && leaderBlockedApplicant.Data)
			{
				return true;
			}

			DatabaseResult<bool> applicantBlockedLeader = await friendService.IsBlockedAsync(applicantID, leaderID);
			return applicantBlockedLeader.IsSuccess && applicantBlockedLeader.Data;
		}

		/// <summary>
		/// Normalises a comma-separated recruitment tag list.
		/// </summary>
		/// <param name="raw">The requested tag string.</param>
		/// <returns>A lower-cased, de-duplicated, comma-separated list within the length cap.</returns>
		/// <remarks>
		/// Lower-cased and de-duplicated because the directory search matches against this column
		/// directly. A guild that wrote "PvP, pvp, PVP" would otherwise occupy three times the
		/// budget of one that wrote it once, for no additional matches.
		/// </remarks>
		private static string NormalizeTags(string raw)
		{
			string cleaned = ChatSanitizer.SanitizeIncoming(raw, GuildTextLimits.MaxTagsLength);
			if (string.IsNullOrWhiteSpace(cleaned))
			{
				return string.Empty;
			}

			string[] parts = cleaned.Split(',');
			List<string> kept = new List<string>(parts.Length);
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			int budget = GuildTextLimits.MaxTagsLength;
			int used = 0;

			for (int i = 0; i < parts.Length; ++i)
			{
				string tag = parts[i].Trim().ToLowerInvariant();
				if (tag.Length == 0 || tag.Length > 24 || !seen.Add(tag))
				{
					continue;
				}

				int cost = tag.Length + (kept.Count > 0 ? 1 : 0);
				if (used + cost > budget)
				{
					break;
				}

				used += cost;
				kept.Add(tag);
			}

			return string.Join(",", kept);
		}

		/// <summary>
		/// Sends a broadcast to a guild's members on this scene server.
		/// </summary>
		/// <typeparam name="T">Broadcast type.</typeparam>
		/// <param name="guildID">The guild.</param>
		/// <param name="broadcast">The message.</param>
		/// <param name="onlyCharacterID">Optional single recipient.</param>
		private void BroadcastToGuildMembers<T>(long guildID, T broadcast, long onlyCharacterID = 0)
			where T : struct, FishNet.Broadcast.IBroadcast
		{
			TryEnqueueMainThread(() =>
			{
				if (Server == null ||
					!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var characterMappingData))
				{
					return;
				}

				if (onlyCharacterID > 0)
				{
					if (characterMappingData.CharactersByID.TryGetValue(onlyCharacterID, out IPlayerCharacter single) &&
						single?.Owner != null)
					{
						Server.NetworkWrapper.Broadcast(single.Owner, broadcast, true, Channel.Reliable);
					}
					return;
				}

				if (!Server.DataContainerRegistry.TryGet<IGuildCharacterMappingData>(out var mappingData) ||
					!mappingData.GuildCharacterTracker.TryGetValue(guildID, out HashSet<long> memberIDs))
				{
					return;
				}

				foreach (long memberID in memberIDs)
				{
					if (characterMappingData.CharactersByID.TryGetValue(memberID, out IPlayerCharacter member) &&
						member?.Owner != null)
					{
						Server.NetworkWrapper.Broadcast(member.Owner, broadcast, true, Channel.Reliable);
					}
				}
			});
		}
	}
}
