namespace CoinsPayRust
{
    using System;

    public enum DecisionAction { Deliver, WaitPlayer, Partial, Expired, Cancelled, Nothing, Mismatch }

    /// <summary>
    /// What to do with an order after reading its invoice. A pure function, so every rule has a
    /// unit test. Delivery needs every check to pass; what is delivered comes from the order
    /// snapshot, so even a forged API answer cannot grant anything the config did not list.
    /// </summary>
    public sealed class Decision
    {
        public readonly DecisionAction Action;
        public readonly string Reason;

        private Decision(DecisionAction action, string reason)
        {
            Action = action;
            Reason = reason;
        }

        public static Decision Decide(Order order, Invoice invoice, bool playerReady)
        {
            if (order == null || invoice == null || order.Id != invoice.Id) return Of(DecisionAction.Nothing, "not our invoice");
            if (order.State != OrderState.Open && order.State != OrderState.Expired) return Of(DecisionAction.Nothing, "already " + order.State);
            if (invoice.Reference == null || invoice.Reference != order.Reference)
            {
                return Of(DecisionAction.Mismatch, "reference " + invoice.Reference + " differs from " + order.Reference);
            }
            if (invoice.Test != order.Test) return Of(DecisionAction.Mismatch, invoice.Test ? "test invoice for a live order" : "live invoice for a test order");
            switch (invoice.Status ?? "")
            {
                case "paid":
                    if (invoice.Currency == null || !string.Equals(invoice.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
                    {
                        return Of(DecisionAction.Mismatch, "currency " + invoice.Currency + " instead of " + order.Currency);
                    }
                    if (invoice.Amount < Text.ParseAmount(order.Price))
                    {
                        return Of(DecisionAction.Mismatch, "amount " + Text.Amount(invoice.Amount) + " below the price " + order.Price);
                    }
                    if (order.NeedsPlayer && !playerReady) return Of(DecisionAction.WaitPlayer, "paid, the player is not in the game");
                    return Of(DecisionAction.Deliver, invoice.PaidLate ? "paid late" : "paid");
                case "expired":
                    return order.State == OrderState.Open ? Of(DecisionAction.Expired, "expired") : Of(DecisionAction.Nothing, "still expired");
                case "cancelled":
                    return Of(DecisionAction.Cancelled, "cancelled");
                case "open":
                    bool news = invoice.AmountPaid > 0 && (order.ReportedPaid == null || invoice.AmountPaid > Text.ParseAmount(order.ReportedPaid));
                    return news ? Of(DecisionAction.Partial, "partly paid") : Of(DecisionAction.Nothing, "open");
                default:
                    return Of(DecisionAction.Nothing, "status " + invoice.Status);
            }
        }

        private static Decision Of(DecisionAction action, string reason) => new Decision(action, reason);

        public override string ToString() => Action + " (" + Reason + ")";
    }
}
