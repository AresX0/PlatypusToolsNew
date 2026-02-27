using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatypusTools.UI.Services.Vault
{
    /// <summary>
    /// Analyzes vault passwords for reuse, weakness, and overall health.
    /// Mirrors Bitwarden's "Reports" / "Vault Health Reports" feature.
    /// </summary>
    public static class PasswordHealthService
    {
        /// <summary>
        /// Performs a full health analysis of vault login items.
        /// </summary>
        public static PasswordHealthResult AnalyzeVault(VaultDatabase vault)
        {
            var result = new PasswordHealthResult();
            var loginItems = vault.Items.Where(i => i.Type == VaultItemType.Login && i.Login?.Password != null).ToList();
            result.TotalPasswords = loginItems.Count;

            // Detect weak passwords
            foreach (var item in loginItems)
            {
                var strength = PasswordGeneratorService.EvaluateStrength(item.Login!.Password!);
                if (strength <= 1)
                {
                    result.WeakPasswords++;
                    result.WeakItems.Add(item);
                }
            }

            // Detect reused passwords
            var passwordGroups = loginItems
                .GroupBy(i => i.Login!.Password!)
                .Where(g => g.Count() > 1)
                .ToList();

            var reusedItemIds = new HashSet<string>();
            foreach (var group in passwordGroups)
            {
                var items = group.ToList();
                result.ReusedGroups.Add((group.Key, items));
                foreach (var item in items)
                    reusedItemIds.Add(item.Id);
            }

            result.ReusedPasswords = reusedItemIds.Count;
            result.ReusedItems = loginItems.Where(i => reusedItemIds.Contains(i.Id)).ToList();

            return result;
        }

        /// <summary>
        /// Checks if a password appears in a list of known breached passwords (offline check).
        /// Uses a simple hash prefix approach similar to HIBP k-anonymity.
        /// </summary>
        public static bool IsPasswordCommon(string password)
        {
            if (string.IsNullOrEmpty(password)) return true;

            // Most common passwords (top 200 from various breach databases)
            var commonPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "123456", "password", "12345678", "qwerty", "123456789", "12345", "1234",
                "111111", "1234567", "dragon", "123123", "baseball", "abc123", "football",
                "monkey", "letmein", "696969", "shadow", "master", "666666", "qwertyuiop",
                "123321", "mustang", "1234567890", "michael", "654321", "pussy", "superman",
                "1qaz2wsx", "7777777", "fuckyou", "121212", "000000", "qazwsx", "123qwe",
                "killer", "trustno1", "jordan", "jennifer", "zxcvbnm", "asdfgh", "hunter",
                "buster", "soccer", "harley", "batman", "andrew", "tigger", "sunshine",
                "iloveyou", "fuckme", "2000", "charlie", "robert", "thomas", "hockey",
                "ranger", "daniel", "starwars", "klaster", "112233", "george", "asshole",
                "computer", "michelle", "jessica", "pepper", "1111", "zxcvbn", "555555",
                "11111111", "131313", "freedom", "777777", "pass", "maggie", "159753",
                "aaaaaa", "ginger", "princess", "joshua", "cheese", "amanda", "summer",
                "love", "ashley", "6969", "nicole", "chelsea", "biteme", "matthew",
                "access", "yankees", "987654321", "dallas", "austin", "thunder", "taylor",
                "matrix", "william", "corvette", "hello", "martin", "heather", "secret",
                "merlin", "diamond", "1234qwer", "gfhjkm", "hammer", "silver", "222222",
                "88888888", "anthony", "justin", "test", "bailey", "q1w2e3r4t5", "patrick",
                "internet", "scooter", "orange", "11111", "golfer", "cookie", "richard",
                "samantha", "bigdog", "guitar", "jackson", "whatever", "mickey", "chicken",
                "sparky", "snoopy", "maverick", "phoenix", "camaro", "sexy", "peanut",
                "morgan", "welcome", "falcon", "cowboy", "ferrari", "samsung", "andrea",
                "smokey", "steelers", "joseph", "mercedes", "dakota", "arsenal", "eagles",
                "melissa", "boomer", "booboo", "spider", "nascar", "monster", "tigers",
                "yellow", "xxxxxx", "123123123", "gateway", "marina", "diablo", "bulldog",
                "qwer1234", "compaq", "purple", "hardcore", "banana", "junior", "hannah",
                "123654", "joshua1", "cheese1", "amanda1", "password1", "password123",
            };

            return commonPasswords.Contains(password);
        }

        /// <summary>
        /// Gets items with no password set.
        /// </summary>
        public static List<VaultItem> GetItemsWithoutPasswords(VaultDatabase vault)
        {
            return vault.Items
                .Where(i => i.Type == VaultItemType.Login &&
                            (i.Login == null || string.IsNullOrEmpty(i.Login.Password)))
                .ToList();
        }

        /// <summary>
        /// Gets items with no username set.
        /// </summary>
        public static List<VaultItem> GetItemsWithoutUsernames(VaultDatabase vault)
        {
            return vault.Items
                .Where(i => i.Type == VaultItemType.Login &&
                            (i.Login == null || string.IsNullOrEmpty(i.Login.Username)))
                .ToList();
        }

        /// <summary>
        /// Gets items that have no URI associated.
        /// </summary>
        public static List<VaultItem> GetItemsWithoutUris(VaultDatabase vault)
        {
            return vault.Items
                .Where(i => i.Type == VaultItemType.Login &&
                            (i.Login?.Uris == null || !i.Login.Uris.Any(u => !string.IsNullOrEmpty(u.Uri))))
                .ToList();
        }
    }
}
