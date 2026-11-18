using UnityEngine.Networking;
using FishMMO.Client.Security;

namespace FishMMO.Client
{
	/// <summary>
	/// Custom SSL certificate handler for UnityWebRequest on the client.
	///
	/// Delegates all validation to <see cref="ClientCertificatePinning"/> which
	/// performs SHA-256(SPKI) pinning plus temporal-validity checks via
	/// BouncyCastle. Configure pins during client bootstrap with
	/// <c>ClientCertificatePinning.Configure(...)</c>.
	/// </summary>
	public class ClientSSLCertificateHandler : CertificateHandler
	{
		protected override bool ValidateCertificate(byte[] certificateData)
		{
			return ClientCertificatePinning.ValidateCertificate(certificateData);
		}
	}
}