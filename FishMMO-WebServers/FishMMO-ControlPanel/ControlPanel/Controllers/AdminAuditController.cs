using FishMMO.ControlPanel.Auth;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// Reads the operator audit log.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Read-only, and there is no route here that writes, edits or deletes a row. Rows are
	/// written by the action that caused them, which is the only thing that knows what happened;
	/// an endpoint that let an operator compose an audit entry by hand would make every entry
	/// worthless.
	/// </para>
	/// <para>
	/// <b>Reading the log is itself not recorded.</b> That is deliberate: every read would write
	/// a row, whose listing would be a read, and the table would fill with the act of looking at
	/// it. Reads are gated at Operator and logged to the application log instead.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/admin/audit")]
	public sealed class AdminAuditController : ControllerBase
	{
		private readonly IAdminAuditService audit;
		private readonly ILogger<AdminAuditController> log;

		public AdminAuditController(IAdminAuditService audit, ILogger<AdminAuditController> log)
		{
			this.audit = audit;
			this.log = log;
		}

		/// <summary>A page of the log, newest first.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Search(
			[FromQuery] string actor,
			[FromQuery] string targetType,
			[FromQuery] string targetId,
			[FromQuery] string action,
			[FromQuery] string outcome,
			[FromQuery] DateTime? from,
			[FromQuery] DateTime? to,
			[FromQuery] int page = 1,
			[FromQuery] int pageSize = 50)
		{
			var query = new AdminAuditQuery
			{
				ActorName = actor,
				TargetType = targetType,
				TargetID = targetId,
				Action = action,
				Succeeded = outcome switch
				{
					"succeeded" => true,
					"refused" => false,
					_ => null,
				},
				FromUtc = from,
				// An inclusive-looking date filter that silently excludes the day itself is a
				// classic way to hide the thing being searched for, so "to" covers its whole day.
				ToUtc = to?.Date.AddDays(1),
				Page = page,
				PageSize = pageSize,
			};

			var result = await audit.SearchAsync(query, HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable,
					new { error = "The audit log could not be read." });
			}

			log.LogInformation("Audit log read by '{Actor}'.", User.Identity?.Name);

			var data = result.Data;
			return Ok(new
			{
				items = data.Items.Select(e => new
				{
					id = e.ID,
					occurredUtc = e.OccurredUtc,
					actor = e.ActorName,
					actorAccessLevel = e.ActorAccessLevel,
					sessionId = e.ActorSessionID,
					action = e.Action,
					targetType = e.TargetType,
					targetId = e.TargetID,
					targetName = e.TargetName,
					reason = e.Reason,
					succeeded = e.Succeeded,
					outcome = e.Outcome,
					details = e.Details,
					ipAddress = e.IpAddress,
					source = e.Source,
				}),
				page = data.Page,
				pageSize = data.PageSize,
				totalCount = data.TotalCount,
			});
		}

		/// <summary>The distinct actions present, for the filter.</summary>
		[HttpGet("actions")]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Actions()
		{
			var result = await audit.FetchActionsAsync(HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable,
					new { error = "The audit log could not be read." });
			}
			return Ok(result.Data);
		}
	}
}
