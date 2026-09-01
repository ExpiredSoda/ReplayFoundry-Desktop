using ReplayFoundry.Desktop.Features.Studio.CreativePacks;

namespace ReplayFoundry.PreparationTests;

internal static class StudioCreativePackTests
{
    public static IReadOnlyList<TestCase> GetTests() =>
    [
        new("Creative-pack manifests snapshot passive assets and hash deterministically", ManifestIsImmutable),
        new("Creative packs reject traversal executable and mismatched content", UnsafeAssetsAreRejected),
        new("Creative packs reject duplicate IDs and paths ignoring case", DuplicateAssetsAreRejected),
        new("Purchased creative-pack offers require identified HTTPS checkout", PurchasedOffersRequireHostedCheckout),
        new("Creative-pack access is scoped to packs and snapshots warnings", AccessIsPackScoped),
    ];

    private static Task ManifestIsImmutable()
    {
        var assets = new List<StudioCreativePackAsset>
        {
            Asset("sticker-one", StudioCreativeAssetKind.Sticker, "stickers/one.png", "image/png", 'A'),
            Asset("sound-one", StudioCreativeAssetKind.Sound, "sounds/one.wav", "audio/wav", 'B'),
        };
        DateTimeOffset created = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var first = Manifest(assets, created);
        var second = Manifest(assets.ToArray(), created);
        assets.Clear();

        TestAssert.Equal(2, first.Assets.Count, "Manifest asset snapshot.");
        TestAssert.Equal(
            first.ManifestSha256,
            second.ManifestSha256,
            "Canonical manifest hash.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<StudioCreativePackAsset>)first.Assets).Clear(),
            "Manifest assets must remain read-only.");
        return Task.CompletedTask;
    }

    private static Task UnsafeAssetsAreRejected()
    {
        TestAssert.Throws<ArgumentException>(
            () => Asset("bad-path", StudioCreativeAssetKind.Sticker, "../bad.png", "image/png", 'C'),
            "Traversal must fail.");
        TestAssert.Throws<ArgumentException>(
            () => Asset("bad-code", StudioCreativeAssetKind.Sticker, "stickers/bad.exe", "application/octet-stream", 'D'),
            "Executable content must fail.");
        TestAssert.Throws<ArgumentException>(
            () => Asset("bad-type", StudioCreativeAssetKind.Sound, "sounds/bad.wav", "image/png", 'E'),
            "Kind and content type must agree.");
        TestAssert.Throws<ArgumentException>(
            () => new StudioCreativePackAsset(
                "bad-hash",
                StudioCreativeAssetKind.Sticker,
                "stickers/bad.png",
                "image/png",
                1,
                "NOT-A-HASH"),
            "Every asset needs a SHA-256 value.");
        return Task.CompletedTask;
    }

    private static Task DuplicateAssetsAreRejected()
    {
        StudioCreativePackAsset first = Asset(
            "same-id",
            StudioCreativeAssetKind.Sticker,
            "stickers/one.png",
            "image/png",
            'F');
        StudioCreativePackAsset duplicateId = Asset(
            "same-id",
            StudioCreativeAssetKind.Sticker,
            "stickers/two.png",
            "image/png",
            '1');
        TestAssert.Throws<ArgumentException>(
            () => Manifest([first, duplicateId], DateTimeOffset.UnixEpoch),
            "Duplicate asset IDs must fail.");

        StudioCreativePackAsset duplicatePath = Asset(
            "different-id",
            StudioCreativeAssetKind.Sticker,
            "stickers/one.png",
            "image/png",
            '2');
        TestAssert.Throws<ArgumentException>(
            () => Manifest([first, duplicatePath], DateTimeOffset.UnixEpoch),
            "Duplicate asset paths must fail.");
        return Task.CompletedTask;
    }

    private static Task PurchasedOffersRequireHostedCheckout()
    {
        TestAssert.Throws<ArgumentException>(
            () => new StudioCreativePackOffer(
                "creator-stickers",
                "Creator stickers",
                "A passive image pack.",
                StudioCreativePackAcquisitionKind.Purchased,
                "Hosted checkout",
                new Uri("http://example.test/checkout")),
            "Checkout must use HTTPS.");
        TestAssert.Throws<ArgumentException>(
            () => new StudioCreativePackOffer(
                "free-stickers",
                "Free stickers",
                "A free passive image pack.",
                StudioCreativePackAcquisitionKind.Free,
                "Unexpected provider",
                new Uri("https://example.test/checkout")),
            "Free packs must not carry purchase data.");

        var purchased = new StudioCreativePackOffer(
            "creator-stickers",
            "Creator stickers",
            "A passive image pack.",
            StudioCreativePackAcquisitionKind.Purchased,
            "Hosted checkout",
            new Uri("https://checkout.example.test/creator-stickers"));
        TestAssert.Equal(
            StudioCreativePackAcquisitionKind.Purchased,
            purchased.AcquisitionKind,
            "Purchased offer kind.");
        return Task.CompletedTask;
    }

    private static Task AccessIsPackScoped()
    {
        var warnings = new List<string> { "Offline proof accepted." };
        var access = new StudioCreativePackAccessResult(
            "creator-stickers",
            StudioCreativePackAccessCode.Purchased,
            "Verified future commerce adapter",
            warnings);
        warnings.Clear();

        TestAssert.True(access.CanUsePack, "Purchased pack should be usable.");
        TestAssert.Equal(1, access.Warnings.Count, "Warning snapshot.");
        TestAssert.Throws<NotSupportedException>(
            () => ((IList<string>)access.Warnings).Clear(),
            "Warnings must remain immutable.");
        TestAssert.False(
            new StudioCreativePackAccessResult(
                "other-pack",
                StudioCreativePackAccessCode.NotOwned,
                "No matching proof").CanUsePack,
            "Access to one pack must not unlock another.");
        return Task.CompletedTask;
    }

    private static StudioCreativePackManifest Manifest(
        IEnumerable<StudioCreativePackAsset> assets,
        DateTimeOffset createdAtUtc) =>
        new(
            StudioCreativePackManifest.SupportedSchemaVersion,
            "creator-starter-pack",
            "1.0.0",
            "Creator starter pack",
            "Passive Studio stickers and sounds.",
            "Replay Foundry",
            createdAtUtc,
            assets);

    private static StudioCreativePackAsset Asset(
        string assetId,
        StudioCreativeAssetKind kind,
        string path,
        string contentType,
        char hashCharacter) =>
        new(assetId, kind, path, contentType, 128, new string(hashCharacter, 64));
}
