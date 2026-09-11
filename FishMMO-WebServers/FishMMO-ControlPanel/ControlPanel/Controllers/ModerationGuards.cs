using System.Security.Claims;
using FishMMO.Auth.Core;
using FishMMO.ControlPanel.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// The rules every moderation action shares about who may act on whom.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Gathered in one place because they are the part that is easy to get subtly wrong and
	/// easy to forget on the fifth endpoint. Each one closes a route from "administering" to
	/// "escalating":
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <b>Not yourself.</b> An operator banning or demoting their own account is either a
	/// mistake or an attempt to look like they were not present.
	/// </description></item>
	/// <item><description>
	/// <b>Not a peer or a superior.</b> The damaging case is not two game masters kicking each
	/// other in a loop; it is a compromised game master account removing the administrators who
	/// would notice.
	/// </description></item>
	/// <item><description>
	/// <b>Not granting at or above your own level.</b> Without this an administrator can mint
	/// administrators, which makes every ceiling above decorative.
	/// </description></item>
	/// </list>
	/// <para>
	/// These are checked against the level the panel read from the database on this very
	/// request, not from anything the client sent.
	/// </para>
	/// </remarks>
	public static class ModerationGuards
	{
		/// <summary>The signed-in operator's access level.</summary>
		public static AccessLevel ActorLevel(ControllerBase controller) =>
			byte.TryParse(controller.User.FindFirstValue(PanelClaims.AccessLevel), out byte level)
				? (AccessLevel)level
				: AccessLevel.Banned;

		/// <summary>
		/// Refuses an action against the operator's own account, or against a peer or superior.
		/// </summary>
		/// <returns>An error result to return, or null when the action may proceed.</returns>
		public static IActionResult Check(ControllerBase controller, string targetAccount, AccessLevel targetLevel)
		{
			string actor = controller.User.Identity?.Name;

			if (string.Equals(actor, targetAccount, StringComparison.OrdinalIgnoreCase))
			{
				return controller.BadRequest(new { error = "You cannot take this action against your own account." });
			}

			AccessLevel actorLevel = ActorLevel(controller);
			if (targetLevel >= actorLevel)
			{
				return controller.StatusCode(StatusCodes.Status403Forbidden, new
				{
					error = $"That account is {targetLevel}; you cannot act on an account at or above your own level.",
				});
			}
			return null;
		}

		/// <summary>Refuses granting a level at or above the operator's own.</summary>
		/// <returns>An error result to return, or null when the grant may proceed.</returns>
		public static IActionResult CheckGrant(ControllerBase controller, AccessLevel granted)
		{
			if (granted >= ActorLevel(controller))
			{
				return controller.StatusCode(StatusCodes.Status403Forbidden, new
				{
					error = $"You cannot grant {granted}; it is at or above your own level.",
				});
			}
			return null;
		}
	}
}
