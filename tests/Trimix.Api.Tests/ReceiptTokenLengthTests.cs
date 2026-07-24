using System.Linq;
using Microsoft.EntityFrameworkCore;
using Trimix.Api.Data;
using Trimix.Contracts;
using Xunit;

namespace Trimix.Api.Tests;

/// <summary>
/// Regression guard for the receipt-token overflow (critical): a real iOS StoreKit bundle receipt is a
/// multi-kilobyte base64 blob. The old schema stored it in a <c>varchar(2048)</c> column carrying the
/// UNIQUE btree index, so a genuine Apple purchase 500'd (length / index-row-size overflow) and no iOS
/// user could ever be granted or restore an entitlement. The token is now unbounded <c>text</c> and the
/// unique/dedup key rides a fixed-length <see cref="ReceiptRecord.TokenSha256"/> hash.
/// </summary>
public sealed class ReceiptTokenLengthTests : IDisposable
{
    private readonly TrimixApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Model_stores_the_token_unbounded_and_keys_uniqueness_on_the_hash()
    {
        _factory.WithDb(db =>
        {
            var entity = db.Model.FindEntityType(typeof(ReceiptRecord))!;

            // Token must be unbounded so a multi-KB Apple receipt fits.
            Assert.Null(entity.FindProperty(nameof(ReceiptRecord.Token))!.GetMaxLength());

            var uniqueIndexes = entity.GetIndexes().Where(i => i.IsUnique).ToList();
            // Uniqueness rides the fixed-length hash…
            Assert.Contains(
                uniqueIndexes,
                i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(ReceiptRecord.TokenSha256));
            // …and NEVER the raw token (a raw-token unique btree index is exactly the bug).
            Assert.DoesNotContain(
                uniqueIndexes,
                i => i.Properties.Any(p => p.Name == nameof(ReceiptRecord.Token)));

            return true;
        });
    }

    [Fact]
    public async Task Multi_kilobyte_receipt_grants_then_restores_and_persists_the_full_token()
    {
        // A ~6 KB purchase id inflates the encoded receipt well past the old 2048 cap AND past Postgres'
        // 2704-byte btree key limit — the exact shape a real Apple bundle receipt takes.
        var token = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Lifetime, new string('A', 6000), DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);
        Assert.True(token.Length > 2704, $"Test token must exceed the btree key limit; was {token.Length}.");

        var (client, _) = await _factory.RegisterAsync("bigreceipt@example.com");

        var granted = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Lifetime, token);
        granted.EnsureSuccessStatusCode();
        Assert.Equal(EntitlementTier.Lifetime, await client.GetTierAsync());

        // Restore (reinstall path): the SAME multi-KB token reconciles idempotently, still Lifetime.
        var restored = await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Lifetime, token);
        restored.EnsureSuccessStatusCode();
        Assert.Equal(EntitlementTier.Lifetime, await client.GetTierAsync());

        // Exactly one receipt row; the FULL token is persisted verbatim and the unique key is its hash.
        _factory.WithDb(db =>
        {
            var receipt = db.Receipts.Single();
            Assert.Equal(token, receipt.Token);
            Assert.Equal(ReceiptTokenHash.Compute(token), receipt.TokenSha256);
            return true;
        });
    }

    [Fact]
    public async Task Two_distinct_large_receipts_dedup_independently_by_hash()
    {
        var (client, _) = await _factory.RegisterAsync("twobig@example.com");

        var proToken = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Pro, new string('P', 5000), DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);
        var lifetimeToken = SandboxReceiptCodec.Encode(
            new SandboxReceiptPayload(ReceiptPlatforms.Sandbox, Skus.Lifetime, new string('L', 5000), DateTime.UtcNow),
            TrimixApiFactory.SandboxSecret);

        (await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Pro, proToken)).EnsureSuccessStatusCode();
        (await client.ValidatePurchaseAsync(ReceiptPlatforms.Sandbox, Skus.Lifetime, lifetimeToken)).EnsureSuccessStatusCode();

        // Two distinct receipts persisted with distinct hashes; Lifetime supersedes Pro.
        Assert.Equal(EntitlementTier.Lifetime, await client.GetTierAsync());
        _factory.WithDb(db =>
        {
            Assert.Equal(2, db.Receipts.Count());
            Assert.Equal(2, db.Receipts.Select(r => r.TokenSha256).Distinct().Count());
            return true;
        });
    }
}
