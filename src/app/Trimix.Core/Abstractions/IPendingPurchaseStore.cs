namespace Trimix.Core.Abstractions;

/// <summary>
/// Durable queue of store receipts whose server validation has not yet succeeded — the safety net
/// for the "store charged the user but <c>POST /purchases/validate</c> failed" window. Persisted
/// (via <see cref="IPreferences"/>) so a receipt survives a process restart and is re-validated on
/// the next launch / restore, guaranteeing a charged user is never left with no entitlement and no
/// recovery path.
/// </summary>
public interface IPendingPurchaseStore
{
    /// <summary>Records a receipt that still needs server reconciliation (idempotent by token).</summary>
    void Add(StoreReceipt receipt);

    /// <summary>All receipts awaiting reconciliation.</summary>
    IReadOnlyList<StoreReceipt> GetAll();

    /// <summary>Drops a receipt once the server has acknowledged it.</summary>
    void Remove(StoreReceipt receipt);
}
