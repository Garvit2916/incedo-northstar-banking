using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Northstar.Application.Abstractions;
using Northstar.Application.Contracts;
using Northstar.Infrastructure.Notifications;
using Northstar.Infrastructure.Storage;

namespace Northstar.Application.Tests;

/// <summary>
/// Covers the AWS adapters without network calls. The SDK interfaces are proxied
/// so request objects can be inspected and no AWS credentials are required.
/// </summary>
public sealed class AwsAdapterTests
{
    private const string Bucket = "northstar-kyc-test";
    private const string TopicArn = "arn:aws:sns:us-east-1:123456789012:northstar-events";

    [Fact]
    public async Task S3_save_uploads_encrypted_content_and_returns_its_hash()
    {
        var content = "document-bytes"u8.ToArray();
        PutObjectRequest? request = null;
        byte[] uploaded = Array.Empty<byte>();

        var client = CreateS3Proxy((method, args) =>
        {
            if (method.Name != "PutObjectAsync")
            {
                throw new InvalidOperationException($"Unexpected S3 call: {method.Name}");
            }

            request = (PutObjectRequest)args[0]!;
            using var buffer = new MemoryStream();
            request.InputStream.CopyTo(buffer);
            uploaded = buffer.ToArray();
            return Task.FromResult(new PutObjectResponse());
        });

        var storage = new S3BlobStorage(client, Bucket);

        var result = await storage.SaveAsync(
            new MemoryStream(content), "customers/1/doc-1", "application/pdf", default);

        Assert.NotNull(request);
        Assert.Equal(Bucket, request.BucketName);
        Assert.Equal("customers/1/doc-1", request.Key);
        Assert.Equal("application/pdf", request.ContentType);
        Assert.Equal(ServerSideEncryptionMethod.AES256, request.ServerSideEncryptionMethod);
        Assert.Equal(content, uploaded);
        Assert.Equal(content.Length, result.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)), result.Sha256);
    }

    [Fact]
    public async Task S3_presigned_url_is_https_get_and_short_lived()
    {
        GetPreSignedUrlRequest? request = null;
        var client = CreateS3Proxy((method, args) =>
        {
            if (method.Name != "GetPreSignedURLAsync")
            {
                throw new InvalidOperationException($"Unexpected S3 call: {method.Name}");
            }

            request = (GetPreSignedUrlRequest)args[0]!;
            return Task.FromResult("https://example.com/signed");
        });

        var storage = new S3BlobStorage(client, Bucket);

        var url = await storage.GetPresignedUrlAsync("customers/1/doc-1", TimeSpan.FromMinutes(5), default);

        Assert.NotNull(url);
        Assert.Equal("https", url.Scheme);
        Assert.NotNull(request);
        Assert.Equal(Bucket, request.BucketName);
        Assert.Equal("customers/1/doc-1", request.Key);
        Assert.Equal(HttpVerb.GET, request.Verb);
        Assert.Equal(Protocol.HTTPS, request.Protocol);
        Assert.NotNull(request.Expires);
        Assert.InRange(request.Expires.Value, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(6));
    }

    [Fact]
    public async Task S3_delete_is_not_supported()
    {
        var storage = new S3BlobStorage(CreateS3Proxy((_, _) => throw new InvalidOperationException()), Bucket);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => storage.DeleteAsync("customers/1/doc-1", default));
    }

    [Fact]
    public async Task Sns_publisher_sends_only_the_sanitized_event_payload()
    {
        PublishRequest? request = null;
        var client = CreateSnsProxy((method, args) =>
        {
            if (method.Name != "PublishAsync")
            {
                throw new InvalidOperationException($"Unexpected SNS call: {method.Name}");
            }

            request = (PublishRequest)args[0]!;
            return Task.FromResult(new PublishResponse());
        });

        var publisher = new SnsNotificationEventPublisher(
            client, TopicArn, NullLogger<SnsNotificationEventPublisher>.Instance);
        var notificationEvent = new NotificationEventPayload(
            "ApplicationApproved", "Notification", null,
            new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero));

        await publisher.PublishAsync(notificationEvent, default);

        Assert.NotNull(request);
        Assert.Equal(TopicArn, request.TopicArn);

        using var json = JsonDocument.Parse(request.Message);
        Assert.Equal("ApplicationApproved", json.RootElement.GetProperty("eventType").GetString());
        Assert.Equal("Notification", json.RootElement.GetProperty("entityType").GetString());
        Assert.Equal(4, json.RootElement.EnumerateObject().Count());

        Assert.True(request.MessageAttributes.TryGetValue("eventType", out var attribute));
        Assert.Equal("String", attribute.DataType);
        Assert.Equal("ApplicationApproved", attribute.StringValue);
    }

    [Fact]
    public void Sns_publisher_requires_a_topic_arn()
    {
        var client = CreateSnsProxy((_, _) => Task.FromResult(new PublishResponse()));

        Assert.Throws<InvalidOperationException>(() => new SnsNotificationEventPublisher(
            client, "", NullLogger<SnsNotificationEventPublisher>.Instance));
    }

    [Fact]
    public async Task Noop_publisher_discards_events_without_an_aws_client()
    {
        INotificationEventPublisher publisher = new NoopNotificationEventPublisher();
        var notificationEvent = new NotificationEventPayload(
            "General", "Notification", null, DateTimeOffset.UtcNow);

        await publisher.PublishAsync(notificationEvent, default);
    }

    // ---------------------------------------------------------------- helpers

    private static IAmazonS3 CreateS3Proxy(Func<MethodInfo, object?[], object?> handler) =>
        CreateProxy<IAmazonS3>(handler);

    private static IAmazonSimpleNotificationService CreateSnsProxy(
        Func<MethodInfo, object?[], object?> handler) =>
        CreateProxy<IAmazonSimpleNotificationService>(handler);

    private static T CreateProxy<T>(Func<MethodInfo, object?[], object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, CapturingProxy>();
        ((CapturingProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    /// <summary>DispatchProxy base used to intercept AWS SDK interface calls.</summary>
    public class CapturingProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } =
            (_, _) => throw new NotSupportedException();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new InvalidOperationException("Missing AWS SDK method metadata.");
            }

            return Handler(targetMethod, args ?? Array.Empty<object?>());
        }
    }
}
