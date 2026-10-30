using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SurveyBackend.Configuration;
using SurveyBackend.Services.Backups;
using Xunit;

namespace SurveyBackend.Tests;

public sealed class OssBackupUploaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SurveyBackend.Tests", Guid.NewGuid().ToString("N"));
    private readonly BackupOptions _options = new() { Enabled = true, Oss = new BackupOssOptions
    {
        Enabled = true, Region = "cn-hangzhou", Bucket = "synthetic-backup-bucket",
        AccessKeyId = "synthetic-id", AccessKeySecret = "synthetic-secret", SecurityToken = "synthetic-token"
    } };
    private string DatabasePath => Path.Combine(_directory, "snapshot.db");

    public OssBackupUploaderTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(DatabasePath, Encoding.UTF8.GetBytes("synthetic database bytes"));
    }

    [Fact]
    public async Task UploadAsync_ValidSnapshot_UploadsSignedDatabaseThenSafeManifest()
    {
        var handler = new RecordingHandler();
        using var uploader = new OssBackupUploader(Options.Create(_options), handler);
        await uploader.UploadAsync(Record(), DatabasePath, default);
        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(".db", handler.Requests[0].Path);
        Assert.EndsWith(".json", handler.Requests[1].Path);
        var manifest = Encoding.UTF8.GetString(handler.Requests[1].Body);
        Assert.Contains(Record().Sha256!, manifest);
        Assert.DoesNotContain("synthetic-secret", manifest);
        Assert.DoesNotContain("synthetic-token", manifest);
        Assert.DoesNotContain(DatabasePath, manifest);
    }

    [Fact]
    public async Task UploadAsync_ServerCrcMismatch_DoesNotPublishCompletionManifest()
    {
        var handler = new RecordingHandler { WrongCrc = true };
        using var uploader = new OssBackupUploader(Options.Create(_options), handler);
        await Assert.ThrowsAnyAsync<Exception>(() => uploader.UploadAsync(Record(), DatabasePath, default));
        Assert.NotEmpty(handler.Requests);
        Assert.All(handler.Requests, request => Assert.EndsWith(".db", request.Path));
    }

    [Fact]
    public async Task UploadAsync_LocalFileChanged_RejectsBeforeNetworkRequest()
    {
        var record = Record();
        File.AppendAllText(DatabasePath, "changed");
        var handler = new RecordingHandler();
        using var uploader = new OssBackupUploader(Options.Create(_options), handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => uploader.UploadAsync(record, DatabasePath, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UploadAsync_DestinationChanged_DoesNotSendOldBackupToNewBucket()
    {
        var handler = new RecordingHandler();
        using var uploader = new OssBackupUploader(Options.Create(_options), handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uploader.UploadAsync(Record() with { OssBucket = "old-bucket" }, DatabasePath, default));
        Assert.Empty(handler.Requests);
    }

    private BackupRecord Record() => new()
    {
        Id = "20261030T000000Z-0123456789abcdef0123456789abcdef", StartedAt = DateTimeOffset.UtcNow,
        OssRegion = _options.Oss.Region, OssBucket = _options.Oss.Bucket,
        OssKey = _options.Oss.Prefix + "/2026/10/30/snapshot.db",
        Size = new FileInfo(DatabasePath).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DatabasePath)))
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, byte[] Body)> Requests { get; } = [];
        public bool WrongCrc { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.True(request.Headers.Contains("Authorization"));
            Assert.True(request.Headers.Contains("x-oss-security-token"));
            Assert.False(request.Headers.Contains("x-oss-server-side-encryption"));
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add((request.RequestUri.AbsolutePath, body));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Headers.TryAddWithoutValidation("x-oss-request-id", "synthetic-request");
            response.Headers.TryAddWithoutValidation("x-oss-hash-crc64ecma", (WrongCrc ? 1UL : Crc(body)).ToString(CultureInfo.InvariantCulture));
            return response;
        }

        private static ulong Crc(byte[] bytes)
        {
            var crc = ulong.MaxValue;
            foreach (var value in bytes)
            {
                crc ^= value;
                for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xC96C5795D7870F42UL);
            }
            return ~crc;
        }
    }
}
