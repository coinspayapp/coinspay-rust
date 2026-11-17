namespace CoinsPayRust
{
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    [JsonConverter(typeof(StringEnumConverter))]
    public enum OrderState
    {
        /// <summary>Waiting for payment.</summary>
        Open,
        /// <summary>The invoice expired; a late payment still delivers.</summary>
        Expired,
        Cancelled,
        /// <summary>Paid; the item needs the player in the game, awake and alive.</summary>
        Waiting,
        /// <summary>Being delivered. Found at startup, it means a crash mid-delivery.</summary>
        Delivering,
        Delivered,
        /// <summary>Crashed mid-delivery: an admin decides with coinspay.redeliver or coinspay.markdone.</summary>
        Review,
    }

    /// <summary>An in-game item of a package: a Rust item shortname and an amount.</summary>
    public sealed class ItemSpec
    {
        [JsonProperty("shortname")] public string Shortname = "";
        [JsonProperty("amount")] public int Amount = 1;
    }

    /// <summary>
    /// An invoice this server created, with a snapshot of the package at purchase time: delivery
    /// uses these groups, items and commands, never the current config and never anything from the API.
    /// </summary>
    public sealed class Order
    {
        public string Id = "";
        public string Number;
        public string Reference = "";
        public string PlayerId = "";
        public string PlayerName = "";
        public string PackageId = "";
        public string PackageName = "";
        public string Price = "0";
        public string Currency = "USD";
        public List<string> Groups = new List<string>();
        public List<string> Permissions = new List<string>();
        public List<ItemSpec> Items = new List<ItemSpec>();
        public List<string> Commands = new List<string>();
        public bool RequireOnline;
        public long DurationMs;
        public List<string> ExpireCommands = new List<string>();
        public string PayUrl = "";
        public bool Test;
        public OrderState State = OrderState.Open;
        public long CreatedAt;
        public long ExpiresAt;
        public long ClosedAt;
        public long LastCheckAt;
        /// <summary>amount_paid already reported to the player, so a partial payment is announced once.</summary>
        public string ReportedPaid;

        /// <summary>Delivery needs the player in the game: items always, commands when asked.</summary>
        [JsonIgnore] public bool NeedsPlayer => Items.Count > 0 || RequireOnline;

        /// <summary>Still worth asking the API about: open, or expired less than a week ago.</summary>
        public bool Watched(long now) => State == OrderState.Open || (State == OrderState.Expired && now - ClosedAt < Store.WatchExpiredMs);

        [JsonIgnore] public string Label => Number ?? Id;
    }

    /// <summary>
    /// A package bought for a time. When it ends the plugin takes back the groups and permissions it
    /// added (Owned) and runs the expire commands, whether the player is online or not.
    /// </summary>
    public sealed class Grant
    {
        public string PlayerId = "";
        public string PlayerName = "";
        public string PackageId = "";
        public string PackageName = "";
        public long ExpiresAt;
        /// <summary>Groups and permissions of the package: "group:vip", "perm:kits.vip".</summary>
        public List<string> Grants = new List<string>();
        /// <summary>The part of Grants the player did not have before: only these are taken back.</summary>
        public List<string> Owned = new List<string>();
        public List<string> ExpireCommands = new List<string>();
        public string LastOrder;
    }

    /// <summary>The fields of a CoinsPay invoice the plugin uses.</summary>
    public sealed class Invoice
    {
        public string Id;
        public string Number;
        public string Status;
        public string Currency;
        public decimal Amount;
        public decimal AmountPaid;
        public decimal AmountDue;
        public string Reference;
        public string PayUrl;
        public bool Test;
        public bool PaidLate;
    }

    /// <summary>GET /shop: the shop and the key that called.</summary>
    public sealed class ShopInfo
    {
        public string Name;
        public bool Test;
        public string Access;
    }
}
