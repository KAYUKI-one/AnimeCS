using System;
using System.Text;

namespace AnimeCS.Accounts
{
    internal static class AccountCredentials
    {
        internal const string IdRule = "ID 须为 2 至 32 个字母、汉字、数字或 . - _";
        internal const string PasswordRule = "密码须为 8 至 128 个字符";

        internal static bool TryNormalizeId(string raw, out string id)
        {
            try { id = (raw ?? "").Trim().Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { id = ""; return false; }
            if (id.Length < 2 || id.Length > 32) return false;
            foreach (char c in id)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') return false;
            return true;
        }

        internal static bool ValidPassword(string value) => value != null && value.Length >= 8 && value.Length <= 128;
    }
}
