using FishMMO.ControlPanel.Auth;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// The landing page's figures.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Only what the panel can actually read. Server population, scene instances and host
	/// health need the world, scene and login server services to grow operator-facing list
	/// methods first, and a dashboard that invented those numbers would be the most damaging
	/// place in the panel to invent anything: it is the page an operator glances at to decide
	/// whether something is wrong.
	/// </para>
	/// <para>
	/// The character figures come from the same rows the game servers write, so "in world"
	/// here means a scene server holds a live session lease, not that a flag was left set.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/dashboard")]
	public sealed class DashboardController : ControllerBase
	{
		private readonly ICharacterService characters;

		public DashboardController(ICharacterService characters)
		{
			this.characters = characters;
		}

		/// <summary>Counts for the dashboard.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Support)]
		public async Task<IActionResult> Get()
		{
			// One row each: the count is the answer, the page is not.
			var total = await characters.SearchAdminAsync(null, true, null, 1, 1, HttpContext.RequestAborted);
			var live = await characters.SearchAdminAsync(null, false, null, 1, 1, HttpContext.RequestAborted);
			var online = await characters.SearchAdminAsync(null, false, true, 1, 1, HttpContext.RequestAborted);
			var recent = await characters.SearchAdminAsync(null, false, null, 1, 8, HttpContext.RequestAborted);

			if (!total.IsSuccess || !live.IsSuccess || !online.IsSuccess || !recent.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable,
					new { error = "The database did not answer." });
			}

			return Ok(new
			{
				characters = new
				{
					total = total.Data.TotalCount,
					live = live.Data.TotalCount,
					deleted = total.Data.TotalCount - live.Data.TotalCount,
					online = online.Data.TotalCount,
				},
				recentlySaved = recent.Data.Items.Select(SupportCharactersController.Summarise),
				// Named so the page can say which parts of the panel are not yet built, rather
				// than leaving an operator to guess why a panel is missing.
				notWired = new[]
				{
					"Server population and per-server control",
					"Scene instances",
					"Host and daemon health",
					"Account moderation",
					"The audit log",
				},
			});
		}
	}
}
