using FishMMO.Auth.Implementation;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.ControlPanel.Services
{
	/// <summary>
	/// Holds the deployment-shared TOTP master KEK, loaded once at startup from
	/// <c>deployment_secrets</c>.
	/// </summary>
	/// <remarks>
	/// This is the same key the LoginServer loads, under the same row key, which is the whole
	/// point: an authenticator enrolled in the game client must verify on the web and the other
	/// way round. Before this key became a deployment secret it was derived per-LoginServer from
	/// a randomly regenerated signing key, so nothing outside the enrolling process could ever
	/// verify a code — see <see cref="TotpMasterKek"/>.
	/// </remarks>
	public sealed class TotpKeyProvider
	{
		/// <summary>The 32-byte master KEK, or null when it could not be loaded.</summary>
		public byte[] MasterKek { get; private set; }

		/// <summary>Why the key is unavailable, when it is.</summary>
		public string LoadError { get; private set; }

		/// <summary>Whether a usable key is loaded.</summary>
		public bool IsAvailable => MasterKek != null && MasterKek.Length == TotpMasterKek.KeyLength;

		/// <summary>
		/// Loads the key from the database. Called once, before the host starts serving.
		/// </summary>
		/// <returns><c>true</c> when a usable key was loaded.</returns>
		public async Task<bool> LoadAsync(IDeploymentSecretService secrets, CancellationToken cancellationToken = default)
		{
			var result = await secrets.FetchAsync(TotpMasterKek.DatabaseKey, cancellationToken);
			string value = result.IsSuccess ? result.Data : null;

			if (!TotpMasterKek.TryDecode(value, out byte[] key, out string error))
			{
				MasterKek = null;
				LoadError = error;
				return false;
			}

			MasterKek = key;
			LoadError = null;
			return true;
		}
	}
}
