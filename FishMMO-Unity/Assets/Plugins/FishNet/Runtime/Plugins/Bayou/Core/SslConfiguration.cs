using System.Security.Authentication;

namespace FishNet.Transporting.Bayou
{

    [System.Serializable]
    public struct SslConfiguration
    {
        public bool Enabled;
        public string CertificatePath;
        public string CertificatePassword;
        public SslProtocols SslProtocol;

        public SslConfiguration(bool enabled, string certPath, string certPassword, SslProtocols sslProtocols)
        {
            Enabled = enabled;
            CertificatePath = certPath;
            CertificatePassword = certPassword;
            SslProtocol = sslProtocols;
        }
    }

}