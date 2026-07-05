using System.Net.Http;
using System.Net.Security;

namespace ParadoxLoLCompanion.Core.Net;

/// <summary>
/// Crea <see cref="HttpClient"/> que confía en el certificado autofirmado de los
/// servicios locales de LoL, pero <b>solo</b> para <c>127.0.0.1</c> / <c>localhost</c>.
/// </summary>
public static class LocalHttpClientFactory
{
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = ValidateLocalhostOnly,
        };

        return new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(3),
        };
    }

    private static bool ValidateLocalhostOnly(
        HttpRequestMessage request,
        System.Security.Cryptography.X509Certificates.X509Certificate2? cert,
        System.Security.Cryptography.X509Certificates.X509Chain? chain,
        SslPolicyErrors errors)
    {
        var host = request.RequestUri?.Host;
        return host is "127.0.0.1" or "localhost";
    }
}
