using System.Security;
using SecureRemotePassword;

namespace FishMMO.Server.Core.Account.SRP
{
	/// <summary>
	/// Holds SRP (Secure Remote Password) authentication data and logic for a server-side session.
	/// </summary>
	public class ServerSrpData
	{
		/// <summary>
		/// Gets the username associated with the SRP session.
		/// </summary>
		public string UserName { get; private set; }

		/// <summary>
		/// Gets the public ephemeral value sent by the client.
		/// </summary>
		public string PublicClientEphemeral { get; private set; }

		/// <summary>
		/// Gets the SRP server instance handling the protocol.
		/// </summary>
		public SrpServer SrpServer { get; private set; }

		/// <summary>
		/// Gets the salt used for the SRP session.
		/// </summary>
		public string Salt { get; private set; }

		/// <summary>
		/// Gets the verifier used for the SRP session.
		/// </summary>
		public string Verifier { get; private set; }

		/// <summary>
		/// Gets the server's ephemeral values for the SRP session.
		/// </summary>
		public SrpEphemeral ServerEphemeral { get; private set; }

		/// <summary>
		/// Gets the SRP session object after proof verification.
		/// </summary>
		public SrpSession Session { get; private set; }

		/// <summary>
		/// Gets or sets the current SRP state.
		/// </summary>
		public SrpState State { get; set; }

		/// <summary>
		/// Initializes a new instance of the <see cref="ServerSrpData"/> class.
		/// </summary>
		/// <param name="parameters">The SRP parameters to use.</param>
		/// <param name="username">The username for the session.</param>
		/// <param name="publicClientEphemeral">The public ephemeral value sent by the client.</param>
		/// <param name="salt">The salt for the session.</param>
		/// <param name="verifier">The verifier for the session.</param>
		public ServerSrpData(SrpParameters parameters, string username, string publicClientEphemeral, string salt, string verifier)
		{
			UserName = username;
			PublicClientEphemeral = publicClientEphemeral;
			SrpServer = new SrpServer(parameters);
			this.Salt = salt;
			this.Verifier = verifier;
			ServerEphemeral = SrpServer.GenerateEphemeral(this.Verifier);
			State = SrpState.SrpVerify;
		}

		/// <summary>
		/// Verifies the client's proof and derives the SRP session, returning the server's proof if successful.
		/// </summary>
		/// <param name="clientProof">The proof sent by the client.</param>
		/// <param name="serverProof">The server's proof to return if verification succeeds, or an error message if it fails.</param>
		/// <returns><c>true</c> if the proof is valid and the session is established; otherwise, <c>false</c>.</returns>
		public bool GetProof(string clientProof, out string serverProof)
		{
			try
			{
				Session = SrpServer.DeriveSession(ServerEphemeral.Secret,
												  PublicClientEphemeral,
												  Salt,
												  UserName,
												  Verifier,
												  clientProof);
				serverProof = Session.Proof;
				return true;
			}
			catch (SecurityException e)
			{
				serverProof = e.Message;
				return false;
			}
		}
	}
}