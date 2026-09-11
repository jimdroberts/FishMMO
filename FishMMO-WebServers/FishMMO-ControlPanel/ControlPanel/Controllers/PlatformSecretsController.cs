using FishMMO.ControlPanel.Auth;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FishMMO.ControlPanel.Controllers
{
	/// <summary>
	/// An inventory of the deployment secrets: which exist, how old they are, what they protect.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>No secret value is readable here, and there is no rotation.</b> Both omissions are
	/// deliberate and neither is an oversight.
	/// </para>
	/// <para>
	/// <b>On values:</b> the panel has no use for the material that would justify putting every
	/// key the shard depends on one serialization mistake away from a browser. The read goes
	/// through <c>FetchInventoryAsync</c>, whose projection does not name the value column, so
	/// the material never enters this process at all — as opposed to being fetched and then
	/// carefully not sent.
	/// </para>
	/// <para>
	/// <b>On rotation:</b> these three keys do not rotate alike, and the machinery that knows
	/// how lives in the LoginServer and the installer rather than here.
	/// <c>totp_master_kek</c> encrypts every enrolled TOTP secret; replacing it without
	/// re-encrypting them locks every player out of two-factor permanently, and no undo exists
	/// because the old key is gone. <c>signing_key_kek</c> has an atomic swap path in the
	/// LoginServer that coordinates with a running server, which a web request cannot join.
	/// A button here would be a one-click, irreversible shard outage, and its presence would
	/// imply somebody had made it safe.
	/// </para>
	/// <para>
	/// What this page is for is the question an operator actually has at three in the morning:
	/// is the key there, and is it the right shape. A missing or truncated key has no other
	/// symptom until something tries to use it and fails somewhere unrelated.
	/// </para>
	/// </remarks>
	[ApiController]
	[Route("api/platform/secrets")]
	public sealed class PlatformSecretsController : ControllerBase
	{
		/// <summary>
		/// The secrets a FishMMO deployment is expected to hold, and what each one protects.
		/// </summary>
		/// <remarks>
		/// Described here rather than left as bare row keys because "you are missing
		/// <c>totp_master_kek</c>" means nothing to somebody who has not read the login server,
		/// and the consequence of its absence is that the panel refuses to start in production.
		/// </remarks>
		private static readonly (string Key, string Protects, bool Required)[] Expected =
		{
			("totp_master_kek", "Encrypts every account's two-factor secret. Without it, two-factor cannot be enrolled or verified, and the Control Panel refuses to start in production.", true),
			("signing_key_kek", "Encrypts the LoginServer's connection signing key, which game servers use to trust a client's session.", true),
			("client_gate_secret", "Gates client connections before authentication.", true),
		};

		private readonly IDeploymentSecretService secrets;
		private readonly ILogger<PlatformSecretsController> log;

		public PlatformSecretsController(IDeploymentSecretService secrets, ILogger<PlatformSecretsController> log)
		{
			this.secrets = secrets;
			this.log = log;
		}

		/// <summary>The inventory. Never a value.</summary>
		[HttpGet]
		[Authorize(Policy = PanelPolicies.Operator)]
		public async Task<IActionResult> Inventory()
		{
			var result = await secrets.FetchInventoryAsync(HttpContext.RequestAborted);
			if (!result.IsSuccess)
			{
				return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The secret inventory could not be read." });
			}

			var present = result.Data.ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);

			/* Driven by the expected list, not by what happens to be in the table, so a secret
			 * that is MISSING appears as a row saying so. A page that only lists what exists
			 * cannot show an absence, and an absence is the failure worth catching. */
			var rows = Expected.Select(e =>
			{
				present.TryGetValue(e.Key, out var found);
				return new
				{
					key = e.Key,
					protects = e.Protects,
					required = e.Required,
					present = found != null,
					valueLength = found?.ValueLength ?? 0,
					createdUtc = found?.CreatedUtc,
					updatedUtc = found?.UpdatedUtc,
				};
			}).ToList();

			// Anything in the table that is not in the expected list. Not an error — a
			// deployment may carry its own — but worth showing rather than hiding.
			var unexpected = result.Data
				.Where(s => !Expected.Any(e => string.Equals(e.Key, s.Key, StringComparison.OrdinalIgnoreCase)))
				.Select(s => new
				{
					key = s.Key,
					protects = (string)null,
					required = false,
					present = true,
					valueLength = s.ValueLength,
					createdUtc = (DateTime?)s.CreatedUtc,
					updatedUtc = (DateTime?)s.UpdatedUtc,
				});

			log.LogInformation("Secret inventory read by '{Actor}'.", User.Identity?.Name);

			return Ok(new
			{
				secrets = rows.Concat(unexpected),
				missingRequired = rows.Count(r => r.required && !r.present),
				/* Stated in the payload so the page can explain the absence of a rotate button
				 * rather than leaving an operator hunting for one. */
				rotationIsNotAvailableHere =
					"Rotation is done by the installer, and for the signing key by the LoginServer's atomic swap. " +
					"Replacing the two-factor master key without re-encrypting every enrolled secret would lock every " +
					"player out of two-factor, irreversibly, so the panel does not offer it.",
			});
		}
	}
}
