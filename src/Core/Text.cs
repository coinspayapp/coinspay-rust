namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>Small text helpers: placeholders, durations, amounts, rich text.</summary>
    public static class Text
    {
        private static readonly Regex Placeholder = new Regex(@"\{([a-z_]+)\}", RegexOptions.Compiled);
        private static readonly Regex DurationPattern = new Regex(@"^(\d+)\s*(mo|s|m|h|d|w)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RichText = new Regex(@"</?(color|size|b|i)(=[^>]*)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SteamId = new Regex(@"^7656119\d{10}$", RegexOptions.Compiled);

        /// <summary>Replaces {key} with values; unknown keys stay as they are.</summary>
        public static string Fill(string template, IDictionary<string, string> values)
        {
            if (template == null) return "";
            if (values == null || values.Count == 0) return template;
            return Placeholder.Replace(template, m =>
            {
                string value;
                return values.TryGetValue(m.Groups[1].Value, out value) && value != null ? value : m.Value;
            });
        }

        public static Dictionary<string, string> Vars(params string[] kv)
        {
            var map = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) map[kv[i]] = kv[i + 1] ?? "";
            return map;
        }

        /// <summary>"30d", "12h", "90m", "2w", "1mo" (30 days); 0 for empty or "0"; -1 for an invalid value.</summary>
        public static long ParseDuration(string s)
        {
            if (s == null) return 0;
            string v = s.Trim();
            if (v.Length == 0 || v == "0") return 0;
            Match m = DurationPattern.Match(v);
            if (!m.Success) return -1;
            long n;
            if (!long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) return -1;
            long unit;
            switch (m.Groups[2].Value.ToLowerInvariant())
            {
                case "s": unit = 1000L; break;
                case "m": unit = 60000L; break;
                case "h": unit = 3600000L; break;
                case "d": unit = 86400000L; break;
                case "w": unit = 7 * 86400000L; break;
                default: unit = 30 * 86400000L; break;
            }
            if (n > long.MaxValue / unit) return -1;
            return n * unit;
        }

        /// <summary>A short human duration: "29 дн. 3 ч" or "29d 3h".</summary>
        public static string Duration(long ms, bool russian)
        {
            long minutes = Math.Max(0, ms) / 60000L;
            long days = minutes / 1440;
            long hours = minutes % 1440 / 60;
            long mins = minutes % 60;
            var sb = new StringBuilder();
            if (days > 0) sb.Append(days).Append(russian ? " дн." : "d");
            if (hours > 0) sb.Append(sb.Length > 0 ? " " : "").Append(hours).Append(russian ? " ч" : "h");
            if (days == 0 && (mins > 0 || sb.Length == 0)) sb.Append(sb.Length > 0 ? " " : "").Append(mins).Append(russian ? " мин" : "m");
            return sb.ToString();
        }

        /// <summary>4.90 becomes "4.9", 5.00 becomes "5".</summary>
        public static string Amount(decimal value)
        {
            string s = value.ToString("0.############################", CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        /// <summary>A price shown to players, at least two decimals: 1.5 becomes "1.50", 5 becomes "5.00", 0.00012 stays.</summary>
        public static string Money(decimal value)
        {
            string s = value.ToString("0.00##########################", CultureInfo.InvariantCulture);
            return s == "-0.00" ? "0.00" : s;
        }

        /// <summary>A stored amount ("1.5") shown to players ("1.50").</summary>
        public static string Money(string amount) => Money(ParseAmount(amount));

        /// <summary>A decimal in invariant format; 0 for anything else.</summary>
        public static decimal ParseAmount(string s)
        {
            decimal d;
            return s != null && decimal.TryParse(s.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out d) ? d : 0m;
        }

        /// <summary>A price from the config: a positive decimal, a string or a number.</summary>
        public static bool TryParsePrice(string s, out decimal price)
        {
            price = 0m;
            return s != null && decimal.TryParse(s.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out price) && price > 0;
        }

        /// <summary>Rich-text tags removed: for the server console and the invoice title.</summary>
        public static string Plain(string s) => s == null ? "" : RichText.Replace(s, "");

        /// <summary>
        /// A player name that is safe inside a console command: Rust names hold any characters, so
        /// quotes, semicolons, backslashes and control characters go. Commands should prefer {steamid}.
        /// </summary>
        public static string CommandSafe(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsControl(c) || c == '"' || c == '\'' || c == ';' || c == '\\' || c == '`') continue;
                sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return s.Length > 32 ? s.Substring(0, 32) : s;
        }

        /// <summary>Rich text of player input shown back in chat must not open tags.</summary>
        public static string Escape(string s) => s == null ? "" : s.Replace("<", "‹").Replace(">", "›");

        public static bool IsSteamId(string s) => s != null && SteamId.IsMatch(s);

        /// <summary>The address a player can type by hand: no scheme and no query, "coinspay.app/pay/k7m2-x9pq-4tza".</summary>
        public static string ShortUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string s = url;
            int q = s.IndexOf('?');
            if (q >= 0) s = s.Substring(0, q);
            if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(8);
            else if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
            return s.TrimEnd('/');
        }

        /// <summary>The URL with lang set, for the payment page in the player's language.</summary>
        public static string WithLang(string url, string lang)
        {
            if (string.IsNullOrEmpty(url)) return "";
            return url + (url.IndexOf('?') >= 0 ? "&" : "?") + "lang=" + lang;
        }

        public static string Cut(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max);
    }
}
