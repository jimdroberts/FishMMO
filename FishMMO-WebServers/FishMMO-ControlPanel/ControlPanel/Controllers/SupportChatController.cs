using FishMMO.ControlPanel.Auth;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Services;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Reads persisted chat for support staff handling a harassment or abuse report.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Read-only, like <see cref="SupportCharactersController"/>, and for the same reason: there
	/// is no route here that edits or deletes a message. Chat is the evidence a report is judged
	/// on, and evidence an operator can quietly amend is not evidence. Retention is the
	/// database's business, not an endpoint's.
	/// </para>
	/// <para>
	/// <b>Reading is not audited.</b> The audit filter records writes; a GET writes nothing and
	/// carries no <c>[Audited]</c> attribute. <see cref="AdminAuditController"/> explains the
	/// rule — every read recorded would write a row whose own listing is a read — and this
	/// endpoint deliberately follows it rather than making an exception of itself.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/support/chat")]
	public sealed class SupportChatController : ControllerBase
	{
		private readonly IChatService chat;

		public SupportChatController(IChatService chat)
		{
			this.chat = chat;
		}

		/// <summary>Searches persisted chat, newest message first.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Search(
			[FromQuery] string characterName,
			[FromQuery] string accountName,
			[FromQuery] byte? channel,
			[FromQuery] string text,
			[FromQuery] DateTime? from,
			[FromQuery] DateTime? to,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 50)
		{
			var query = new ChatAdminQuery
			{
				CharacterName = characterName,
				AccountName = accountName,
				Channel = channel,
				Text = text,
				FromUtc = from,
				// An inclusive-looking date filter that silently excludes the day itself is a
				// classic way to hide the thing being searched for, so "to" covers its whole day.
				// Same convention as the audit log, so the two date pickers behave identically.
				ToUtc = to?.Date.AddDays(1),
				Page = page,
				PageSize = pageSize,
			};

			var result = await chat.SearchAdminAsync(query, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				/* The service's message is shown rather than replaced. Its one validation refusal —
				 * a message search with nothing to narrow it — names the three filters that would
				 * make the search runnable, and an operator who is told "that search could not be
				 * run" instead just retries the same thing. */
				return BadRequest(new { error = result.ErrorMessage ?? "That search could not be run." });
			}

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(Summarise),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>The list projection: what a table row shows, and nothing more.</summary>
		internal static object Summarise(ChatAdminData m) => new
		{
			id = m.ID,
			characterId = m.CharacterID,
			characterName = m.CharacterName,
			account = m.AccountName,
			channel = m.Channel,
			// Both, deliberately. The number is what a filter round-trips and what a bug report
			// quotes; the name is the only half an operator reading a report can act on, because
			// "channel 5" does not say whether this was shouted at the whole world or whispered.
			channelName = ChannelName(m.Channel),
			message = m.Message,
			serverReceivedUtc = m.ServerReceivedTime,
			timeUtc = m.TimeCreated,
			worldServerId = m.WorldServerID,
			sceneServerId = m.SceneServerID,
		};

		/// <summary>Names a raw channel value.</summary>
		/// <remarks>
		/// An undefined value is reported as itself rather than guessed at or blanked: a row
		/// written by a newer server with a channel this build has no name for still has to read
		/// back as something an operator can see and quote.
		/// </remarks>
		private static string ChannelName(byte channel) =>
			Enum.IsDefined(typeof(ChatChannel), channel)
				? ((ChatChannel)channel).ToString()
				: $"Channel {channel}";
	}
}
