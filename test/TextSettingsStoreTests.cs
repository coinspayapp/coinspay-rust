using System.Collections.Generic;
using System.Linq;
using CoinsPayRust;
using Newtonsoft.Json;
using Xunit;

namespace CoinsPayRust.Tests
{
    public class TextTests
    {
        [Fact]
        public void FillsKnownPlaceholdersOnly() =>
            Assert.Equal("give 7656 wood {x}", Text.Fill("give {steamid} {item} {x}", Text.Vars("steamid", "7656", "item", "wood")));

        [Theory]
        [InlineData("30d", 30L * 86400000L)]
        [InlineData("12h", 12L * 3600000L)]
        [InlineData("90m", 90L * 60000L)]
        [InlineData("2w", 14L * 86400000L)]
        [InlineData("1mo", 30L * 86400000L)]
        [InlineData("45s", 45000L)]
        [InlineData("", 0L)]
        [InlineData("0", 0L)]
        [InlineData("3x", -1L)]
        [InlineData("d", -1L)]
        public void ParsesDurations(string text, long ms) => Assert.Equal(ms, Text.ParseDuration(text));

        [Fact]
        public void FormatsDurations()
        {
            Assert.Equal("29d 3h", Text.Duration(29L * 86400000L + 3L * 3600000L + 120000L, false));
            Assert.Equal("29 дн. 3 ч", Text.Duration(29L * 86400000L + 3L * 3600000L, true));
            Assert.Equal("2h 5m", Text.Duration(2L * 3600000L + 5L * 60000L, false));
            Assert.Equal("0m", Text.Duration(-5, false));
        }

        [Fact]
        public void FormatsAmounts()
        {
            Assert.Equal("4.9", Text.Amount(4.90m));
            Assert.Equal("5", Text.Amount(5.000m));
            Assert.Equal("0.00012", Text.Amount(0.00012m));
            Assert.Equal(4.99m, Text.ParseAmount("4.99"));
            Assert.Equal(0m, Text.ParseAmount("4,99"));
            Assert.Equal("1.50", Text.Money(1.5m));
            Assert.Equal("5.00", Text.Money(5m));
            Assert.Equal("4.99", Text.Money(4.990m));
            Assert.Equal("0.00012", Text.Money(0.00012m));
            Assert.Equal("1.50", Text.Money("1.5"));
        }

        [Fact]
        public void ShortensTheLink()
        {
            Assert.Equal("coinspay.app/pay/k7m2-x9pq-4tza", Text.ShortUrl("https://coinspay.app/pay/k7m2-x9pq-4tza?lang=ru"));
            Assert.Equal("https://coinspay.app/pay/a?lang=ru", Text.WithLang("https://coinspay.app/pay/a", "ru"));
            Assert.Equal("https://x/pay/a?b=1&lang=en", Text.WithLang("https://x/pay/a?b=1", "en"));
        }

        [Fact]
        public void MakesNamesSafeForCommands()
        {
            Assert.Equal("Bob quit", Text.CommandSafe("Bob\"; quit"));
            Assert.Equal("Вася", Text.CommandSafe("Вася\n"));
            Assert.Equal(32, Text.CommandSafe(new string('a', 50)).Length);
        }

        [Fact]
        public void StripsRichText() => Assert.Equal("CoinsPay paid", Text.Plain("<color=#10b981>CoinsPay</color> <size=16><b>paid</b></size>"));

        [Fact]
        public void KnowsSteamIds()
        {
            Assert.True(Text.IsSteamId("76561198000000001"));
            Assert.False(Text.IsSteamId("7656119800000000"));
            Assert.False(Text.IsSteamId("Bob"));
        }
    }

    public class SettingsTests
    {
        [Fact]
        public void DefaultsAreValid()
        {
            Settings s = Settings.From(ConfigFile.Defaults(), null);
            Assert.Equal(new[] { "vip-30", "wood", "coins" }, s.Packages.Select(p => p.Id));
            Assert.Single(s.Problems); // only the missing key
            Assert.False(s.Configured);
            PackageDef vip = s.Package("VIP-30");
            Assert.Equal(30L * 86400000L, vip.DurationMs);
            Assert.Equal(new[] { "vip" }, vip.Groups);
            Assert.True(s.Package("wood").NeedsPlayer);
            Assert.False(s.Package("coins").NeedsPlayer);
            Assert.Equal(new[] { "donate" }, s.ChatCommands);
        }

        [Fact]
        public void OwnPackagesReplaceTheSamples()
        {
            string json = "{\"api\":{\"key\":\"cpk_test_0123456789\"},\"chat-commands\":[\"/Store\",\"shop\"],\"menu\":\"chat\","
                + "\"packages\":{\"kit\":{\"name\":\"Kit\",\"price\":2,\"commands\":[\"kit give {steamid} starter\"],\"require-online\":true}}}";
            var file = JsonConvert.DeserializeObject<ConfigFile>(json);
            Settings s = Settings.From(file, null);
            Assert.Equal(new[] { "kit" }, s.Packages.Select(p => p.Id));
            Assert.Equal(new[] { "store", "shop" }, s.ChatCommands);
            Assert.True(s.ChatMenu);
            Assert.True(s.Configured);
            Assert.True(s.TestKey);
            Assert.Equal(2m, s.Package("kit").Price);
            Assert.True(s.Package("kit").NeedsPlayer);
            Assert.Empty(s.Problems);
        }

        [Fact]
        public void BadPackagesAreReported()
        {
            var file = new ConfigFile
            {
                Api = new ConfigFile.ApiConfig { Key = "cpk_live_0123456789", Url = "ftp://x" },
                ServerId = "main server",
                Currency = "dollars",
                LifetimeMinutes = 1,
                Packages = new Dictionary<string, PackageConfig>
                {
                    ["free"] = new PackageConfig { Price = "0", Commands = new List<string> { "say hi" } },
                    ["bad id"] = new PackageConfig { Price = "1", Commands = new List<string> { "say hi" } },
                    ["empty"] = new PackageConfig { Price = "1" },
                    ["dlc"] = new PackageConfig { Price = "1", Items = new List<ItemSpec> { new ItemSpec { Shortname = "abovegroundpool", Amount = 1 } } },
                    ["when"] = new PackageConfig { Price = "1", Duration = "soon", Groups = new List<string> { "vip" } },
                    ["timed-item"] = new PackageConfig { Price = "1", Duration = "7d", Items = new List<ItemSpec> { new ItemSpec { Shortname = "wood", Amount = 5 } } },
                    ["ok"] = new PackageConfig { Price = "1.5", Groups = new List<string> { "VIP" }, Permissions = new List<string> { "Kits.VIP" }, ExpireCommands = new List<string> { "say bye" } },
                },
            };
            Settings s = Settings.From(file, name => name == "abovegroundpool" ? "is DLC content" : null);
            Assert.Equal(new[] { "timed-item", "ok" }, s.Packages.Select(p => p.Id));
            Assert.Equal(Settings.DefaultUrl, s.ApiUrl);
            Assert.Equal("main", s.ServerId);
            Assert.Equal("USD", s.Currency);
            Assert.Equal(15, s.LifetimeMinutes);
            Assert.Equal(new[] { "vip" }, s.Package("ok").Groups);
            Assert.Equal(new[] { "kits.vip" }, s.Package("ok").Permissions);
            string all = string.Join("\n", s.Problems);
            Assert.Contains("api.url", all);
            Assert.Contains("server-id", all);
            Assert.Contains("packages.free.price", all);
            Assert.Contains("packages.bad id", all);
            Assert.Contains("packages.empty: nothing to deliver", all);
            Assert.Contains("abovegroundpool is DLC content", all);
            Assert.Contains("packages.when.duration", all);
            Assert.Contains("packages.timed-item: duration ends nothing", all);
            Assert.Contains("packages.ok: expire-commands without duration", all);
        }

        [Fact]
        public void TheDefaultConfigRoundTrips()
        {
            string json = JsonConvert.SerializeObject(ConfigFile.Defaults(), Formatting.Indented);
            Assert.DoesNotContain("null", json);
            Assert.Contains("\"deposit {steamid} 1000\"", json);
            var back = JsonConvert.DeserializeObject<ConfigFile>(json);
            Assert.Equal(3, back.Packages.Count);
            Assert.Single(back.ChatCommands);
        }
    }

    public class StoreTests
    {
        [Fact]
        public void DeliveringBecomesReviewOnLoad()
        {
            var store = new Store();
            store.Load(null);
            store.Add(new Order { Id = "a", State = OrderState.Delivering });
            store.Add(new Order { Id = "b", State = OrderState.Open });
            string json = store.Serialize();
            Assert.Contains("\"Delivering\"", json);

            var again = new Store();
            List<Order> crashed = again.Load(json);
            Assert.Single(crashed);
            Assert.Equal(OrderState.Review, again.Order("a").State);
            Assert.Equal(OrderState.Open, again.Order("b").State);
        }

        [Fact]
        public void DamagedFileThrows() => Assert.ThrowsAny<JsonException>(() => new Store().Load("{\"Orders\": [ {\"Id\": "));

        [Fact]
        public void FindsByNumberAndPrunesOldClosedOrders()
        {
            var store = new Store();
            store.Load("");
            long now = 100L * 86400000L;
            store.Add(new Order { Id = "old", Number = "T-1", State = OrderState.Delivered, ClosedAt = now - 31L * 86400000L });
            store.Add(new Order { Id = "new", State = OrderState.Delivered, ClosedAt = now - 86400000L });
            store.Add(new Order { Id = "review", State = OrderState.Review, ClosedAt = now - 90L * 86400000L });
            Assert.Equal("old", store.Order("t-1").Id);
            Assert.True(store.Prune(now));
            Assert.Null(store.Order("old"));
            Assert.NotNull(store.Order("new"));
            Assert.NotNull(store.Order("review"));
            Assert.False(store.Prune(now));
        }

        [Fact]
        public void KeepsPermanentGrants()
        {
            var store = new Store();
            store.Load(null);
            store.AddPermanent("1", "group:vip");
            store.AddPermanent("1", "group:vip");
            var again = new Store();
            again.Load(store.Serialize());
            Assert.True(again.IsPermanent("1", "group:vip"));
            Assert.False(again.IsPermanent("1", "perm:x"));
        }
    }
}
