using CoinsPayRust;
using Xunit;

namespace CoinsPayRust.Tests
{
    public class DecisionTests
    {
        private static Order NewOrder(bool items = false) => new Order
        {
            Id = "inv_1",
            Reference = "rust:main:76561198000000001:vip:abcdef0123",
            Price = "4.99",
            Currency = "USD",
            Test = true,
            Items = items ? new System.Collections.Generic.List<ItemSpec> { new ItemSpec { Shortname = "wood", Amount = 10 } } : new System.Collections.Generic.List<ItemSpec>(),
        };

        private static Invoice Paid(string amount = "4.99") => new Invoice
        {
            Id = "inv_1",
            Status = "paid",
            Currency = "USD",
            Amount = Text.ParseAmount(amount),
            AmountPaid = Text.ParseAmount(amount),
            Reference = "rust:main:76561198000000001:vip:abcdef0123",
            Test = true,
        };

        [Fact]
        public void PaidDelivers() => Assert.Equal(DecisionAction.Deliver, Decision.Decide(NewOrder(), Paid(), false).Action);

        [Fact]
        public void PaidLateDeliversWithReason()
        {
            Invoice i = Paid();
            i.PaidLate = true;
            Order o = NewOrder();
            o.State = OrderState.Expired;
            Decision d = Decision.Decide(o, i, true);
            Assert.Equal(DecisionAction.Deliver, d.Action);
            Assert.Equal("paid late", d.Reason);
        }

        [Fact]
        public void ItemsWaitForThePlayer()
        {
            Assert.Equal(DecisionAction.WaitPlayer, Decision.Decide(NewOrder(items: true), Paid(), false).Action);
            Assert.Equal(DecisionAction.Deliver, Decision.Decide(NewOrder(items: true), Paid(), true).Action);
        }

        [Fact]
        public void RequireOnlineWaitsToo()
        {
            Order o = NewOrder();
            o.RequireOnline = true;
            Assert.Equal(DecisionAction.WaitPlayer, Decision.Decide(o, Paid(), false).Action);
        }

        [Fact]
        public void ForeignReferenceIsAMismatch()
        {
            Invoice i = Paid();
            i.Reference = "rust:other:1:vip:0000000000";
            Assert.Equal(DecisionAction.Mismatch, Decision.Decide(NewOrder(), i, true).Action);
            i.Reference = null;
            Assert.Equal(DecisionAction.Mismatch, Decision.Decide(NewOrder(), i, true).Action);
        }

        [Fact]
        public void TestModeMustMatch()
        {
            Invoice i = Paid();
            i.Test = false;
            Assert.Equal(DecisionAction.Mismatch, Decision.Decide(NewOrder(), i, true).Action);
        }

        [Fact]
        public void CurrencyMustMatch()
        {
            Invoice i = Paid();
            i.Currency = "EUR";
            Assert.Equal(DecisionAction.Mismatch, Decision.Decide(NewOrder(), i, true).Action);
            i.Currency = "usd";
            Assert.Equal(DecisionAction.Deliver, Decision.Decide(NewOrder(), i, true).Action);
        }

        [Fact]
        public void AmountBelowThePriceIsAMismatch()
        {
            Assert.Equal(DecisionAction.Mismatch, Decision.Decide(NewOrder(), Paid("4.98"), true).Action);
            Assert.Equal(DecisionAction.Deliver, Decision.Decide(NewOrder(), Paid("5.00"), true).Action);
        }

        [Fact]
        public void OtherInvoiceOrClosedOrderDoNothing()
        {
            Invoice i = Paid();
            i.Id = "inv_2";
            Assert.Equal(DecisionAction.Nothing, Decision.Decide(NewOrder(), i, true).Action);
            foreach (OrderState s in new[] { OrderState.Delivered, OrderState.Delivering, OrderState.Waiting, OrderState.Review, OrderState.Cancelled })
            {
                Order o = NewOrder();
                o.State = s;
                Assert.Equal(DecisionAction.Nothing, Decision.Decide(o, Paid(), true).Action);
            }
        }

        [Fact]
        public void ExpiryAndCancellation()
        {
            Invoice i = Paid();
            i.Status = "expired";
            Assert.Equal(DecisionAction.Expired, Decision.Decide(NewOrder(), i, true).Action);
            Order expired = NewOrder();
            expired.State = OrderState.Expired;
            Assert.Equal(DecisionAction.Nothing, Decision.Decide(expired, i, true).Action);
            i.Status = "cancelled";
            Assert.Equal(DecisionAction.Cancelled, Decision.Decide(NewOrder(), i, true).Action);
        }

        [Fact]
        public void PartialPaymentIsReportedOncePerAmount()
        {
            Invoice i = Paid();
            i.Status = "open";
            i.AmountPaid = 2m;
            Order o = NewOrder();
            Assert.Equal(DecisionAction.Partial, Decision.Decide(o, i, true).Action);
            o.ReportedPaid = "2";
            Assert.Equal(DecisionAction.Nothing, Decision.Decide(o, i, true).Action);
            i.AmountPaid = 3m;
            Assert.Equal(DecisionAction.Partial, Decision.Decide(o, i, true).Action);
            i.AmountPaid = 0m;
            Assert.Equal(DecisionAction.Nothing, Decision.Decide(NewOrder(), i, true).Action);
        }
    }
}
