using System.Net;
using ExitPass.CentralPms.Application.StatutoryEvidence;
using ExitPass.CentralPms.Infrastructure.StatutoryEvidence;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.StatutoryEvidence;

public sealed class StatutoryEvidencePreviewStorageAdapterTests
{
    [Fact]
    public async Task OpenObjectContentStreamAsync_DoesNotReadOrBufferProviderContentBeforeCallerStreams()
    {
        var providerStream = new TrackingReadStream([0xff, 0xd8, 0xff, 0xd9]);
        using var handler = new StaticResponseHandler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(providerStream)
            };
            response.Content.Headers.ContentType = new("image/jpeg");
            response.Content.Headers.ContentLength = 4;
            response.Headers.TryAddWithoutValidation("x-amz-checksum-sha256", "qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqo=");
            response.Headers.TryAddWithoutValidation("x-amz-version-id", "version-1");
            return response;
        });
        using var client = new HttpClient(handler);
        var adapter = new S3CompatibleStatutoryEvidenceObjectStorageAdapter(
            client,
            Options.Create(new StatutoryEvidenceUploadOptions
            {
                Endpoint = "http://127.0.0.1:19000",
                AccessKeyId = "disposable-access",
                SecretAccessKey = "disposable-secret"
            }));

        var content = await adapter.OpenObjectContentStreamAsync(
            new StatutoryEvidenceObjectContentRequest("private-bucket", "internal/object", 1024),
            CancellationToken.None);

        providerStream.BytesRead.Should().Be(0);
        content.Content.CanSeek.Should().BeFalse();
        var buffer = new byte[2];
        (await content.Content.ReadAsync(buffer)).Should().Be(2);
        providerStream.BytesRead.Should().Be(2);

        await content.DisposeAsync();
        providerStream.Disposed.Should().BeTrue();
    }

    private sealed class StaticResponseHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory());
    }

    private sealed class TrackingReadStream(byte[] bytes) : Stream
    {
        private int _offset;
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _offset; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, bytes.Length - _offset);
            if (count <= 0)
            {
                return ValueTask.FromResult(0);
            }

            bytes.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            BytesRead += count;
            return ValueTask.FromResult(count);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
