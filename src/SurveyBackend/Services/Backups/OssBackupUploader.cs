using System.Security.Cryptography;
using System.Text.Json;
using AlibabaCloud.OSS.V2;
using AlibabaCloud.OSS.V2.Credentials;
using AlibabaCloud.OSS.V2.Models;

namespace SurveyBackend.Services.Backups;

public sealed class OssBackupUploader : IBackupUploader, IDisposable
{
    private readonly BackupOssOptions _options;
    private readonly Client? _client;

    public OssBackupUploader(IOptions<BackupOptions> options) : this(options, null) { }

    internal OssBackupUploader(IOptions<BackupOptions> options, HttpMessageHandler? handler)
    {
        _options = options.Value.Oss;
        if (!options.Value.Enabled || !_options.Enabled)
            return;
        var configuration = AlibabaCloud.OSS.V2.Configuration.LoadDefault();
        configuration.Region = _options.Region;
        configuration.Endpoint = string.IsNullOrWhiteSpace(_options.Endpoint) ? null : _options.Endpoint;
        configuration.CredentialsProvider = new StaticCredentialsProvider(
            _options.AccessKeyId, _options.AccessKeySecret, _options.SecurityToken);
        configuration.RetryMaxAttempts = 3;
        configuration.ConnectTimeout = TimeSpan.FromSeconds(15);
        configuration.ReadWriteTimeout = options.Value.UploadTimeout;
        if (handler is not null) configuration.HttpTransport = new AlibabaCloud.OSS.V2.Transport.HttpTransport(handler);
        _client = new Client(configuration);
    }

    public async Task UploadAsync(BackupRecord record, string databasePath, CancellationToken cancellationToken)
    {
        if (_client is null || record.OssBucket != _options.Bucket || record.OssRegion != _options.Region ||
            record.OssEndpoint != _options.Endpoint || !record.OssKey.StartsWith(_options.Prefix + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("OssDestinationChanged");

        await using var file = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (file.Length != record.Size || hash != record.Sha256)
            throw new InvalidDataException("BackupHashMismatch");
        file.Position = 0;
        await PutAsync(record.OssKey, file, cancellationToken);
        // 清单最后上传；它是远端快照完成的标志，不包含凭据和本地运行状态。
        using var manifest = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
        {
            record.Id, record.StartedAt, record.CompletedAt, record.Size, record.Sha256,
            record.FullCheck, record.ApplicationVersion, record.Migrations
        }));
        await PutAsync(Path.ChangeExtension(record.OssKey, ".json"), manifest, cancellationToken);
    }

    private async Task PutAsync(string key, Stream body, CancellationToken cancellationToken)
    {
        var result = await _client!.PutObjectAsync(new PutObjectRequest
        {
            Bucket = _options.Bucket, Key = key, Body = body
        }, cancellationToken: cancellationToken);
        if ((int)result.StatusCode is < 200 or >= 300)
            throw new IOException("OssUploadFailed");
    }

    public void Dispose() => _client?.Dispose();
}
