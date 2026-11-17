namespace CoinsPayRust
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>A failed API call: an HTTP error with the CoinsPay error code, or a network failure (status 0).</summary>
    public sealed class ApiError
    {
        public readonly int Status;
        public readonly string Code;
        public readonly string Message;

        public ApiError(int status, string code, string message)
        {
            Status = status;
            Code = code;
            Message = message ?? "";
        }

        /// <summary>Worth retrying with the same Idempotency-Key: the network, 5xx or a rate limit.</summary>
        public bool Retryable => Status == 0 || Status >= 500 || Status == 429;

        public bool RateLimited => Status == 429;

        public override string ToString() => Status == 0 ? "network error: " + Message : "HTTP " + Status + " " + Code + ": " + Message;
    }

    /// <summary>The CoinsPay merchant API. Asynchronous: answers come back through the host on the main thread.</summary>
    public sealed class ApiClient
    {
        private const int MaxAttempts = 3;

        private readonly IHost host;
        private readonly string baseUrl;
        private readonly string key;
        private readonly string userAgent;

        public ApiClient(IHost host, string baseUrl, string key, string userAgent)
        {
            this.host = host;
            this.baseUrl = baseUrl.TrimEnd('/');
            this.key = key;
            this.userAgent = userAgent;
        }

        /// <summary>GET /shop: the shop name and the key mode and access.</summary>
        public void Shop(Action<ShopInfo, ApiError> done)
        {
            Request("GET", "/shop", null, null, (o, error) =>
            {
                if (error != null)
                {
                    done(null, error);
                    return;
                }
                var shop = o["shop"] as JObject;
                var k = o["key"] as JObject;
                done(new ShopInfo { Name = Str(shop, "name"), Test = Bool(k, "test"), Access = Str(k, "access") }, null);
            });
        }

        /// <summary>POST /invoices. The Idempotency-Key makes a retry return the same invoice.</summary>
        public void CreateInvoice(JObject body, string idempotencyKey, Action<Invoice, ApiError> done)
        {
            CreateAttempt(body.ToString(Formatting.None), idempotencyKey, 1, done);
        }

        private void CreateAttempt(string body, string idempotencyKey, int attempt, Action<Invoice, ApiError> done)
        {
            Request("POST", "/invoices", body, idempotencyKey, (o, error) =>
            {
                if (error == null)
                {
                    Unwrap(o, done);
                    return;
                }
                if (!error.Retryable || error.RateLimited || attempt >= MaxAttempts)
                {
                    done(null, error);
                    return;
                }
                host.After(attempt, () => CreateAttempt(body, idempotencyKey, attempt + 1, done));
            });
        }

        public void Invoice(string id, Action<Invoice, ApiError> done)
        {
            Request("GET", "/invoices/" + Uri.EscapeDataString(id), null, null, (o, error) =>
            {
                if (error != null) done(null, error);
                else Unwrap(o, done);
            });
        }

        /// <summary>The newest paid invoices of the shop, one page.</summary>
        public void PaidInvoices(int limit, Action<List<Invoice>, ApiError> done)
        {
            Request("GET", "/invoices?status=paid&limit=" + limit.ToString(CultureInfo.InvariantCulture), null, null, (o, error) =>
            {
                if (error != null)
                {
                    done(null, error);
                    return;
                }
                var list = new List<Invoice>();
                var items = o["invoices"] as JArray;
                if (items != null)
                {
                    foreach (JToken t in items)
                    {
                        var inv = t as JObject;
                        if (inv != null) list.Add(Parse(inv));
                    }
                }
                done(list, null);
            });
        }

        /// <summary>Single-invoice answers are {"invoice": {...}}.</summary>
        private static void Unwrap(JObject o, Action<Invoice, ApiError> done)
        {
            var inv = o["invoice"] as JObject;
            if (inv == null) done(null, new ApiError(200, "BAD_RESPONSE", "no invoice in the response"));
            else done(Parse(inv), null);
        }

        public static Invoice Parse(JObject o)
        {
            return new Invoice
            {
                Id = Str(o, "id"),
                Number = Str(o, "number"),
                Status = Str(o, "status"),
                Currency = Str(o, "currency"),
                Amount = Text.ParseAmount(Str(o, "amount")),
                AmountPaid = Text.ParseAmount(Str(o, "amount_paid")),
                AmountDue = Text.ParseAmount(Str(o, "amount_due")),
                Reference = Str(o, "reference"),
                PayUrl = Str(o, "pay_url"),
                Test = Bool(o, "test"),
                PaidLate = Bool(o, "paid_late"),
            };
        }

        private void Request(string method, string path, string body, string idempotencyKey, Action<JObject, ApiError> done)
        {
            var headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + key,
                ["Accept"] = "application/json",
                ["User-Agent"] = userAgent,
            };
            if (idempotencyKey != null) headers["Idempotency-Key"] = idempotencyKey;
            if (body != null) headers["Content-Type"] = "application/json";
            host.Http(method, baseUrl + path, body, headers, (status, text) =>
            {
                if (status == 0)
                {
                    done(null, new ApiError(0, "NETWORK", text));
                    return;
                }
                if (status >= 200 && status < 300)
                {
                    JObject parsed = ParseObject(text);
                    if (parsed == null) done(null, new ApiError(status, "BAD_RESPONSE", "not a JSON object"));
                    else done(parsed, null);
                    return;
                }
                done(null, Error(status, text ?? ""));
            });
        }

        private static ApiError Error(int status, string text)
        {
            string code = "HTTP_" + status.ToString(CultureInfo.InvariantCulture);
            string message = Text.Cut(text, 200);
            // Not our JSON (a proxy or Cloudflare page) leaves the HTTP status as the code.
            var err = ParseObject(text)?["error"] as JObject;
            if (err != null)
            {
                code = Str(err, "code") ?? code;
                message = Str(err, "message") ?? message;
            }
            return new ApiError(status, code, message);
        }

        /// <summary>A JSON object with numbers as decimals, or null for anything else (a proxy page, a cut body).</summary>
        public static JObject ParseObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(text)) { FloatParseHandling = FloatParseHandling.Decimal, DateParseHandling = DateParseHandling.None })
                {
                    return JToken.ReadFrom(reader) as JObject;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Str(JObject o, string key)
        {
            JToken t = o?[key];
            if (t == null || t.Type == JTokenType.Null) return null;
            return t.Type == JTokenType.String ? (string)t : t.ToString(Formatting.None);
        }

        private static bool Bool(JObject o, string key)
        {
            JToken t = o?[key];
            return t != null && t.Type == JTokenType.Boolean && (bool)t;
        }
    }
}
