// [Confluence] Tests for Tranquillity #373 (gloebit#103): an exception while a state request is being processed must not
// leave the transaction in the race-condition map, or every later callback for it answers "pending" for ever.
// Gloebit has no tests upstream; this drives GloebitTransaction.ProcessStateRequest directly. The transaction is put in
// the in-memory cache that Get reads first, so no database is involved.

using System;
using System.Collections.Generic;
using System.Reflection;
using Gloebit.GloebitMoneyModule;
using OpenMetaverse;
using Xunit;

namespace InWorldz.Phlox.Tests;

public class GloebitStateRequestTests
{
    /// <summary>Returns the default for everything; for the alert interface, which only notifies.</summary>
    public class Quiet : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type rt = method.ReturnType;
            return rt == typeof(void) || !rt.IsValueType ? null : Activator.CreateInstance(rt);
        }
    }

    /// <summary>An asset callback whose enact step throws, as a grid's own callback might.</summary>
    public class ThrowsOnEnact : DispatchProxy
    {
        public int EnactCalls;

        protected override object Invoke(MethodInfo method, object[] args)
        {
            if (method.Name == nameof(GloebitTransaction.IAssetCallback.processAssetEnactHold))
            {
                EnactCalls++;
                throw new InvalidOperationException("asset callback failed");
            }
            Type rt = method.ReturnType;
            return rt == typeof(void) || !rt.IsValueType ? null : Activator.CreateInstance(rt);
        }
    }

    private static GloebitTransaction CachedTransaction()
    {
        var txn = new GloebitTransaction
        {
            TransactionID = UUID.Random(),
            CallbackKey = UUID.Random().ToString(),
        };
        var map = (Dictionary<string, GloebitTransaction>)typeof(GloebitTransaction)
            .GetField("s_transactionMap", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);
        lock (map)
            map[txn.TransactionID.ToString()] = txn;
        return txn;
    }

    [Fact]
    public void An_enact_that_throws_does_not_leave_the_transaction_pending()
    {
        GloebitTransaction txn = CachedTransaction();
        var assets = DispatchProxy.Create<GloebitTransaction.IAssetCallback, ThrowsOnEnact>();
        var alerts = DispatchProxy.Create<GloebitAPIWrapper.ITransactionAlert, Quiet>();

        Assert.ThrowsAny<InvalidOperationException>(() =>
            GloebitTransaction.ProcessStateRequest(txn.TransactionID.ToString(), "enact", txn.CallbackKey, assets, alerts, out _));

        // Gloebit retries. Before the fix the retry found the leaked entry and answered "pending" without reaching the
        // enact step again; now it reaches it (and the callback throws again).
        Assert.ThrowsAny<InvalidOperationException>(() =>
            GloebitTransaction.ProcessStateRequest(txn.TransactionID.ToString(), "enact", txn.CallbackKey, assets, alerts, out _));
        Assert.Equal(2, ((ThrowsOnEnact)(object)assets).EnactCalls);
    }

    [Fact]
    public void An_unrecognized_state_is_refused_and_leaves_nothing_pending()
    {
        GloebitTransaction txn = CachedTransaction();
        var assets = DispatchProxy.Create<GloebitTransaction.IAssetCallback, Quiet>();
        var alerts = DispatchProxy.Create<GloebitAPIWrapper.ITransactionAlert, Quiet>();

        Assert.False(GloebitTransaction.ProcessStateRequest(txn.TransactionID.ToString(), "nonsense", txn.CallbackKey, assets, alerts, out string first));
        Assert.Equal("Unrecognized state request", first);
        Assert.False(GloebitTransaction.ProcessStateRequest(txn.TransactionID.ToString(), "nonsense", txn.CallbackKey, assets, alerts, out string second));
        Assert.Equal("Unrecognized state request", second);
    }
}
