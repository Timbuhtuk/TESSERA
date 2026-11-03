using System.Text.Json;
using DomainColorTest;

namespace PixelArtAlignment.Tests;

public static class UnrealSyncChecks
{
    public static void Run(Action<string, Action> check)
    {
        check("Unreal bridge transfers PNG, rejects incomplete downloads and tracks revisions", RoundTrip);
    }

    private static void RoundTrip()
    {
        string root = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "tessera-unreal-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string project = Path.Combine(root, "Game.uproject");
            File.WriteAllText(project, "{}");
            var bridge = new UnrealSyncBridge(Path.Combine(root, "library", "unreal-sync.json"));
            bridge.SelectProject(project);
            using var image = new Bitmap(2, 2);
            image.SetPixel(0, 0, Color.FromArgb(128, 220, 30, 50));
            string source = Path.Combine(root, "source.png");
            image.Save(source, System.Drawing.Imaging.ImageFormat.Png);
            string unrealPath = bridge.SendPng(source, Guid.Parse("11111111-2222-3333-4444-555555555555"), "Stone.png");
            if (!unrealPath.StartsWith("/Game/TesseraSync/T_Stone_", StringComparison.Ordinal)) throw new Exception("Unexpected Unreal path");
            string name = unrealPath.Split('/')[^1];
            string outgoing = Path.Combine(bridge.BridgeDirectory, "to_unreal");
            var sent = JsonSerializer.Deserialize<UnrealSyncBridge.Transfer>(File.ReadAllText(Path.Combine(outgoing, name + ".json")))!;
            if (sent.Sha256 != UnrealSyncBridge.Hash(Path.Combine(outgoing, name + ".png"))) throw new Exception("Outgoing hash mismatch");
            bridge.SendPng(source, Guid.Parse("11111111-2222-3333-4444-555555555555"), "Stone.png");
            var resent = JsonSerializer.Deserialize<UnrealSyncBridge.Transfer>(File.ReadAllText(Path.Combine(outgoing, name + ".json")))!;
            if (resent.Revision == sent.Revision || resent.Sha256 != sent.Sha256) throw new Exception("Explicit resend was not distinguishable");

            string incoming = Path.Combine(bridge.BridgeDirectory, "to_tessera");
            Directory.CreateDirectory(incoming);
            string receivedPng = Path.Combine(incoming, name + ".png");
            File.Copy(source, receivedPng);
            sent.Sha256 = UnrealSyncBridge.Hash(receivedPng);
            File.WriteAllText(Path.Combine(incoming, name + ".json"), JsonSerializer.Serialize(sent));
            if (bridge.PendingFromUnreal().Count != 1) throw new Exception("Valid incoming PNG was not found");
            File.AppendAllText(receivedPng, "corrupt");
            if (bridge.PendingFromUnreal().Count != 0) throw new Exception("Partial or changed PNG was accepted");
            File.Copy(source, receivedPng, true);
            bridge.MarkReceived(sent);
            if (bridge.PendingFromUnreal().Count != 0) throw new Exception("Received revision was duplicated");
            image.SetPixel(1, 1, Color.Blue);
            image.Save(receivedPng, System.Drawing.Imaging.ImageFormat.Png);
            sent.Sha256 = UnrealSyncBridge.Hash(receivedPng);
            File.WriteAllText(Path.Combine(incoming, name + ".json"), JsonSerializer.Serialize(sent));
            if (new UnrealSyncBridge(Path.Combine(root, "library", "unreal-sync.json")).PendingFromUnreal().Count != 1)
                throw new Exception("Updated Unreal texture was not detected after restart");
        }
        finally { Directory.Delete(root, true); }
    }
}
