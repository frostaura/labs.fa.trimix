using System.Text.Json;
using Trimix.Core.Abstractions;

namespace Trimix.Core.Billing;

/// <summary>
/// <see cref="IPendingPurchaseStore"/> backed by <see cref="IPreferences"/> (a JSON-serialized list
/// under <see cref="PreferenceKeys.PendingReceipts"/>). Survives process restart so a receipt that
/// failed post-purchase validation is re-tried on the next launch / restore.
/// </summary>
public sealed class PendingPurchaseStore(IPreferences preferences) : IPendingPurchaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lock _gate = new();

    public void Add(StoreReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (_gate)
        {
            var list = Load();
            if (!list.Any(r => SameReceipt(r, receipt)))
            {
                list.Add(receipt);
                Save(list);
            }
        }
    }

    public IReadOnlyList<StoreReceipt> GetAll()
    {
        lock (_gate)
        {
            return Load();
        }
    }

    public void Remove(StoreReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (_gate)
        {
            var list = Load();
            if (list.RemoveAll(r => SameReceipt(r, receipt)) > 0)
            {
                Save(list);
            }
        }
    }

    private static bool SameReceipt(StoreReceipt a, StoreReceipt b) =>
        a.Platform == b.Platform && a.SkuId == b.SkuId && a.Token == b.Token;

    private List<StoreReceipt> Load()
    {
        var raw = preferences.Get(PreferenceKeys.PendingReceipts);
        if (string.IsNullOrEmpty(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<StoreReceipt>>(raw, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<StoreReceipt> list) =>
        preferences.Set(PreferenceKeys.PendingReceipts, JsonSerializer.Serialize(list, JsonOptions));
}
