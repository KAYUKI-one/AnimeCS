using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
#nullable enable

namespace AnimeCS.Match
{
    public static class BattleTicket
    {
        public static string Issue(string secret, string room, long generation, long expires, int seat, string id,
            string? map = null, string? revision = null)
        {
            string text = string.Join("|", room, expires.ToString(CultureInfo.InvariantCulture),
                seat.ToString(CultureInfo.InvariantCulture), id, generation.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(map) && !string.IsNullOrEmpty(revision)) text += "|" + map + "|" + revision;
            var payload = Encoding.UTF8.GetBytes(text);
            using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(payload) + "." + Convert.ToBase64String(mac.ComputeHash(payload));
        }

        public static bool Read(string secret, string ticket, out string room, out int seat, out string playerId, out long generation,
            string? expectedMap = null, string? expectedRevision = null)
        {
            room = string.Empty;
            playerId = string.Empty;
            generation = 0;
            seat = -1;
            if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(ticket) || ticket.Length > 512) return false;
            try
            {
                string[] parts = ticket.Split('.');
                if (parts.Length != 2) return false;
                byte[] data = Convert.FromBase64String(parts[0]);
                using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                if (!CryptographicOperations.FixedTimeEquals(mac.ComputeHash(data), Convert.FromBase64String(parts[1]))) return false;
                string[] values = Encoding.UTF8.GetString(data).Split('|');
                if (values.Length != 5 && values.Length != 7 ||
                    expectedMap != null && (values.Length != 7 || values[5] != expectedMap || values[6] != expectedRevision) ||
                    !Guid.TryParseExact(values[0], "N", out _) || values[3].Length == 0 ||
                    !long.TryParse(values[4], NumberStyles.None, CultureInfo.InvariantCulture, out generation) || generation < 1 ||
                    !long.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out long expires) ||
                    expires < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
                    !int.TryParse(values[2], NumberStyles.None, CultureInfo.InvariantCulture, out seat) || seat < 0) return false;
                room = values[0];
                playerId = values[3];
                return true;
            }
            catch (FormatException) { return false; }
        }
    }
}
