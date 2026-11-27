using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace AnimeCS.Networking
{
    internal static class ServiceEndpoint
    {
        internal static bool AllowPrivateHttp(bool configured = false)
        {
            string setting = Environment.GetEnvironmentVariable("ANIMECS_ALLOW_PRIVATE_HTTP");
            return setting == null ? configured : setting == "1" || string.Equals(setting,"true",StringComparison.OrdinalIgnoreCase);
        }

        internal static HttpClientHandler Handler() => new HttpClientHandler { UseProxy=false, AllowAutoRedirect=false };

        internal static string Resolve(string url, int defaultPort, bool allowPrivateHttp)
        {
            if (string.IsNullOrEmpty(url)) return "http://127.0.0.1:" + defaultPort;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("服务地址无效");
            if (uri.Scheme == Uri.UriSchemeHttp && !PermitsHttp(uri, allowPrivateHttp))
                throw new ArgumentException("远程服务需要 HTTPS。可信私有局域网可显式启用 HTTP 并填写私有 IP 地址。");
            return uri.AbsoluteUri.TrimEnd('/');
        }

        private static bool PermitsHttp(Uri uri, bool allowPrivateHttp)
        {
            if (uri.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IPAddress.TryParse(uri.DnsSafeHost, out var ip)) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            if (IPAddress.IsLoopback(ip)) return true;
            if (!allowPrivateHttp) return false;
            byte[] bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == AddressFamily.InterNetworkV6) return (bytes[0] & 0xfe) == 0xfc;
            return bytes[0] == 10 || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                bytes[0] == 192 && bytes[1] == 168;
        }
    }
}
