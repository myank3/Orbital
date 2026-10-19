using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Orbital.Service;

public record PresignedUpload(string Key, string Url, DateTime ExpiresAt);

public class R2StorageService : IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public R2StorageService(IConfiguration cfg)
    {
        var accountId = cfg["R2:AccountId"]
            ?? throw new InvalidOperationException("R2:AccountId missing from configuration.");
        _bucket = cfg["R2:BucketName"]
            ?? throw new InvalidOperationException("R2:BucketName missing from configuration.");
        var keyId = cfg["R2:AccessKeyId"]
            ?? throw new InvalidOperationException("R2:AccessKeyId missing from configuration.");
        var secret = cfg["R2:SecretAccessKey"]
            ?? throw new InvalidOperationException("R2:SecretAccessKey missing from configuration.");

        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(keyId, secret),
            new AmazonS3Config
            {
                ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
            });
    }

    /// <summary>
    /// Presign a PUT URL for direct browser → R2 upload.
    /// The client MUST send the exact same Content-Type header,
    /// because it is signed into the URL.
    /// </summary>
    public PresignedUpload PresignPut(string key, string contentType, TimeSpan ttl)
    {
        var expiresAt = DateTime.UtcNow.Add(ttl);
        var req = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = contentType,
        };
        return new PresignedUpload(key, _s3.GetPreSignedURL(req), expiresAt);
    }

    /// <summary>
    /// Presign GET URLs for a batch of keys. Deduplicates and skips nulls.
    /// </summary>
    public Dictionary<string, string> PresignGetBatch(IEnumerable<string> keys, TimeSpan ttl)
    {
        var expiresAt = DateTime.UtcNow.Add(ttl);
        var result = new Dictionary<string, string>();
        foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct())
        {
            var req = new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = expiresAt,
            };
            result[key] = _s3.GetPreSignedURL(req);
        }
        return result;
    }

    public Task DeleteAsync(string key) => _s3.DeleteObjectAsync(_bucket, key);

    public void Dispose() => _s3.Dispose();
}