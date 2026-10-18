using System.Security.Cryptography;
using ParadoxLoLCompanion.Core.Update;

namespace ParadoxLoLCompanion.Tests;

public class UpdateManifestTests
{
    [Theory]
    [InlineData("2.5.5", "2.5.5", null)]
    [InlineData("2.5.5\n", "2.5.5", null)]
    [InlineData("# comentario\n\n  2.5.5  \r\n", "2.5.5", null)]
    [InlineData("2.5.5 # release de octubre", "2.5.5", null)]
    [InlineData("2.5.5\tAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "2.5.5",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("2.5.5 notahash", "2.5.5", null)]
    public void Parse_reads_version_and_optional_hash(string text, string version, string? sha)
    {
        var m = UpdateManifest.Parse(text);
        Assert.NotNull(m);
        Assert.Equal(Version.Parse(version), m!.Version);
        Assert.Equal(sha, m.Sha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# solo comentarios\n")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("v2.5.5")]
    public void Parse_rejects_garbage(string text) =>
        Assert.Null(UpdateManifest.Parse(text));

    private static string TempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), "plc-update-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] FakeExe(int size)
    {
        var bytes = new byte[size];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    [Fact]
    public void Verify_accepts_plausible_exe_with_matching_length_and_hash()
    {
        var bytes = FakeExe((int)DownloadVerifier.MinPlausibleBytes + 10);
        var path = TempFile(bytes);
        try
        {
            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.Null(DownloadVerifier.Verify(path, bytes.Length, sha));
            Assert.Null(DownloadVerifier.Verify(path, null, null));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_rejects_truncated_download()
    {
        var path = TempFile(FakeExe((int)DownloadVerifier.MinPlausibleBytes + 10));
        try { Assert.Contains("truncada", DownloadVerifier.Verify(path, 50_000_000, null)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_rejects_captive_portal_html()
    {
        var path = TempFile("<!DOCTYPE html><html>login</html>"u8.ToArray());
        try { Assert.NotNull(DownloadVerifier.Verify(path, null, null)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_rejects_non_pe_payload_of_plausible_size()
    {
        var bytes = new byte[DownloadVerifier.MinPlausibleBytes + 1];
        var path = TempFile(bytes);
        try { Assert.Contains("MZ", DownloadVerifier.Verify(path, null, null)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_rejects_hash_mismatch()
    {
        var path = TempFile(FakeExe((int)DownloadVerifier.MinPlausibleBytes + 1));
        try { Assert.Contains("SHA-256", DownloadVerifier.Verify(path, null, new string('0', 64))); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_reports_missing_file() =>
        Assert.NotNull(DownloadVerifier.Verify(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid()), null, null));
}
