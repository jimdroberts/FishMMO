using System.Security;
using SecureRemotePassword;

namespace FishMMO.Client
{
	public class ClientSrpData
	{
		public SrpClient SrpClient { get; private set; }
		public SrpEphemeral ClientEphemeral { get; private set; }
		public SrpSession Session { get; private set; }

		public ClientSrpData(SrpParameters parameters)
		{
			SrpClient = new SrpClient(parameters);
			ClientEphemeral = SrpClient.GenerateEphemeral();
		}

		public void GetSaltAndVerifier(string username, string password, out string salt, out string verifier)
		{
			salt = SrpClient.GenerateSalt();
			string privateKey  = SrpClient.DerivePrivateKey(salt, username, password);
			verifier = SrpClient.DeriveVerifier(privateKey);
		}

		public bool GetProof(string username, string password, string salt, string serverPublicEphemeral, out string proof)
		{
			string privateKey = SrpClient.DerivePrivateKey(salt, username, password);
			try
			{
				Session = SrpClient.DeriveSession(ClientEphemeral.Secret,
												  serverPublicEphemeral,
												  salt,
												  username,
												  privateKey);
				proof = Session.Proof;
				return true;
			}
			catch (SecurityException e)
			{
				proof = e.Message;
				return false;
			}
		}

		public bool Verify(string serverProof, out string result)
		{
			try
			{
				SrpClient.VerifySession(ClientEphemeral.Public, Session, serverProof);
				result = "Srp Successfully verified session.";
				return true;
			}
			catch (SecurityException e)
			{
				result = e.Message;
				return false;
			}
		}
	}
}