using UnityEngine.Networking;

namespace FishMMO.Client
{
	public class ClientSSLCertificateHandler : CertificateHandler
	{
		protected override bool ValidateCertificate(byte[] certificateData)
		{
			// Return true to accept the certificate, or false to reject it.
			return true;
		}
	}
}