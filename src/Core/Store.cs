namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;

    /// <summary>
    /// The data file: orders, timed grants and permanent grants. The only source of truth about
    /// which invoices belong to this server. The plugin saves it after every change.
    /// </summary>
    public sealed class Store
    {
        public const long WatchExpiredMs = 7L * 86400000L;
        private const long KeepClosedMs = 30L * 86400000L;

        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        private Data data = new Data();

        /// <summary>Loads the file; returns orders found mid-delivery, now moved to Review. Throws JsonException on a damaged file.</summary>
        public List<Order> Load(string json)
        {
            var crashed = new List<Order>();
            data = string.IsNullOrWhiteSpace(json) ? new Data() : JsonConvert.DeserializeObject<Data>(json, Json) ?? new Data();
            if (data.Orders == null) data.Orders = new List<Order>();
            if (data.Grants == null) data.Grants = new List<Grant>();
            if (data.Permanent == null) data.Permanent = new Dictionary<string, List<string>>();
            data.Orders.RemoveAll(o => o == null || string.IsNullOrEmpty(o.Id));
            data.Grants.RemoveAll(g => g == null || string.IsNullOrEmpty(g.PlayerId));
            foreach (Order o in data.Orders)
            {
                if (o.State == OrderState.Delivering)
                {
                    o.State = OrderState.Review;
                    crashed.Add(o);
                }
            }
            return crashed;
        }

        public string Serialize() => JsonConvert.SerializeObject(data, Json);

        public void Add(Order order) => data.Orders.Add(order);

        /// <summary>By CoinsPay id or invoice number.</summary>
        public Order Order(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Order o in data.Orders) if (o.Id == id || string.Equals(id, o.Number, StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }

        public List<Order> Orders() => new List<Order>(data.Orders);

        public List<Grant> Grants() => new List<Grant>(data.Grants);

        public int OpenFor(string playerId)
        {
            int n = 0;
            foreach (Order o in data.Orders) if (o.State == OrderState.Open && o.PlayerId == playerId) n++;
            return n;
        }

        public Grant Grant(string playerId, string packageId)
        {
            foreach (Grant g in data.Grants) if (g.PlayerId == playerId && g.PackageId == packageId) return g;
            return null;
        }

        public void AddGrant(Grant grant) => data.Grants.Add(grant);

        public void RemoveGrant(Grant grant) => data.Grants.Remove(grant);

        /// <summary>Groups and permissions bought forever: "group:vip". A timed package never takes these back.</summary>
        public bool IsPermanent(string playerId, string token)
        {
            List<string> list;
            return data.Permanent.TryGetValue(playerId, out list) && list.Contains(token);
        }

        public void AddPermanent(string playerId, string token)
        {
            List<string> list;
            if (!data.Permanent.TryGetValue(playerId, out list)) data.Permanent[playerId] = list = new List<string>();
            if (!list.Contains(token)) list.Add(token);
        }

        /// <summary>Drops delivered, cancelled and expired orders closed more than 30 days ago.</summary>
        public bool Prune(long now)
        {
            int removed = data.Orders.RemoveAll(o =>
                o.ClosedAt > 0 && now - o.ClosedAt > KeepClosedMs
                && (o.State == OrderState.Delivered || o.State == OrderState.Cancelled || o.State == OrderState.Expired));
            return removed > 0;
        }

        private sealed class Data
        {
            public int Version = 1;
            public List<Order> Orders = new List<Order>();
            public List<Grant> Grants = new List<Grant>();
            public Dictionary<string, List<string>> Permanent = new Dictionary<string, List<string>>();
        }
    }
}
