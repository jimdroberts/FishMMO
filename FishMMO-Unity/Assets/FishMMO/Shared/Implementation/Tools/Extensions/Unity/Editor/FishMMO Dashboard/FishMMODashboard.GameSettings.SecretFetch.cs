#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Npgsql.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared
{
	/// <summary>
	/// Game Settings ▸ Client Secret ▸ Fetch from Database: reads the gate secret the servers use.
	/// </summary>
	/// <remarks>
	/// The client signs every web API request with this secret, and IPFetch and the Patcher check the signature
	/// against <c>deployment_secrets.client_gate_secret</c>. A pasted value that was not that row's (a secret from an
	/// earlier database, say, after Configure Server Keys made a new one) fails every request with 401
	/// "signature mismatch". Fetching it from the same database the servers read removes the copy step.
	/// The value is put in the field as an unsaved draft, never logged; Write Secret to File persists it as before.
	/// </remarks>
	public partial class FishMMODashboard
	{
		/// <summary>The deployment_secrets key the web servers verify against (IPFetch, Patcher).</summary>
		private const string ClientGateSecretKey = "client_gate_secret";

		private static readonly Regex GeneratedSecret = new Regex("values\\.Secret = \"([^\"]*)\";", RegexOptions.Compiled);

		private void FetchClientSecretFromDatabase(TextField secretField)
		{
			gs_secretStatusLabel.text = "Reading the gate secret from the database…";
			gs_secretStatusLabel.style.color = new Color(0.7f, 0.7f, 0.7f, 1f);

			(string secret, string error) result;
			try
			{
				// Off the editor thread: the database layer is async, and blocking on it from Unity's
				// synchronisation context could deadlock. Bounded, so an unreachable database cannot hang the editor.
				Task<(string, string)> fetch = Task.Run(ReadGateSecretAsync);
				result = fetch.Wait(TimeSpan.FromSeconds(15))
					? fetch.Result
					: (null, "the database did not answer within 15 s. Check that PostgreSQL is running and the credentials are set (FISHMMO_DB_USERNAME / FISHMMO_DB_PASSWORD or the secrets file).");
			}
			catch (Exception ex)
			{
				result = (null, (ex.InnerException ?? ex).Message);
			}

			if (result.secret == null)
			{
				gs_secretStatusLabel.text = "Could not fetch the secret: " + result.error;
				gs_secretStatusLabel.style.color = new Color(0.95f, 0.4f, 0.4f, 1f);
				return;
			}
			if (!IsValidBase64Secret(result.secret))
			{
				gs_secretStatusLabel.text = "The database holds a gate secret that is not valid base64 of at least 32 bytes; re-run Installer ▸ Database ▸ 9 : Configure Server Keys.";
				gs_secretStatusLabel.style.color = new Color(0.95f, 0.4f, 0.4f, 1f);
				return;
			}

			// The field's change callback stores it as the session draft.
			secretField.value = result.secret;

			string written = ReadWrittenSecret();
			bool same = written != null && string.Equals(written, result.secret, StringComparison.Ordinal);
			gs_secretStatusLabel.text = same
				? "Fetched: identical to the secret already in ClientApiSecret.generated.cs. Nothing to write."
				: written == null
					? "Fetched. No secret is written yet: Write Secret to File, then rebuild the clients."
					: "Fetched: DIFFERENT from ClientApiSecret.generated.cs, so current clients are refused (401). Write Secret to File, then rebuild the clients.";
			gs_secretStatusLabel.style.color = same ? new Color(0.4f, 0.85f, 0.4f, 1f) : new Color(0.95f, 0.8f, 0.35f, 1f);
		}

		/// <summary>Reads the row the web servers verify against, through the servers' own database configuration.</summary>
		private static async Task<(string, string)> ReadGateSecretAsync()
		{
			IConfiguration configuration = DatabaseConfigurationHelper.BuildDesignTimeConfiguration();
			await using var database = new FishMMO.Database.Database(configuration);
			if (!database.ServiceRegistry.TryGet(out IDeploymentSecretService secrets))
			{
				return (null, "the deployment secret service is not registered.");
			}
			DatabaseResult<string> row = await secrets.FetchAsync(ClientGateSecretKey).ConfigureAwait(false);
			if (row.IsSuccess && !string.IsNullOrWhiteSpace(row.Data))
			{
				return (row.Data.Trim(), null);
			}
			return row.ErrorCode == DatabaseErrorCodes.NotFound || row.IsSuccess
				? (null, $"there is no '{ClientGateSecretKey}' in deployment_secrets. Run Installer ▸ Database ▸ 9 : Configure Server Keys.")
				: (null, $"[{row.ErrorCode}] {row.ErrorMessage}");
		}

		/// <summary>The secret ClientApiSecret.generated.cs currently holds, or null.</summary>
		private static string ReadWrittenSecret()
		{
			try
			{
				string path = GetClientSecretFilePath();
				if (!File.Exists(path)) return null;
				Match match = GeneratedSecret.Match(File.ReadAllText(path));
				return match.Success ? match.Groups[1].Value : null;
			}
			catch (IOException)
			{
				return null;
			}
		}
	}
}
#endif
