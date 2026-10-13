using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AgentBridge.Transport;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

/// <summary>
/// The hand-written HTTP/1.1 request parser and the loopback Host/Origin guard.
/// </summary>
public class BridgeHttpRequestReaderTests
{
    private static Task<BridgeHttpRequest?> Read(string raw) =>
        BridgeHttpRequestReader.ReadAsync(new MemoryStream(Encoding.ASCII.GetBytes(raw)), CancellationToken.None);

    [Fact]
    public async Task ContentLengthBody_IsReadExactly()
    {
        var request = await Read("POST /mcp?x=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 4\r\n\r\nbodyEXTRA");

        request.ShouldNotBeNull();
        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/mcp");
        request.GetHeader("host").ShouldBe("127.0.0.1");
        Encoding.ASCII.GetString(request.Body).ShouldBe("body");
        request.KeepAlive.ShouldBeTrue("HTTP/1.1 defaults to keep-alive");
    }

    [Fact]
    public async Task ChunkedBody_IsDecoded()
    {
        var request = await Read(
            "POST /mcp HTTP/1.1\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n3\r\nabc\r\n2;ext=1\r\nde\r\n0\r\n\r\n");

        request.ShouldNotBeNull();
        Encoding.ASCII.GetString(request.Body).ShouldBe("abcde");
        request.KeepAlive.ShouldBeFalse();
    }

    [Fact]
    public async Task EmptyStream_ReturnsNull()
    {
        (await Read("")).ShouldBeNull();
    }

    [Theory]
    [InlineData("GARBAGE\r\n\r\n", 400)]
    [InlineData("POST /mcp HTTP/1.1\r\nContent-Length: 99999999\r\n\r\n", 413)]
    [InlineData("POST /mcp HTTP/1.1\r\nBadHeaderWithoutColon\r\n\r\n", 400)]
    public async Task MalformedOrOversized_ThrowsWithStatus(string raw, int status)
    {
        var ex = await Should.ThrowAsync<BridgeHttpException>(() => Read(raw));

        ex.StatusCode.ShouldBe(status);
    }

    [Theory]
    [InlineData("127.0.0.1:39271", true)]
    [InlineData("localhost", true)]
    [InlineData("[::1]:39271", true)]
    [InlineData("attacker.example:39271", false)]
    [InlineData("", false)]
    public void LoopbackHost_IsRecognised(string host, bool expected)
    {
        LoopbackRequestGuard.IsLoopbackHost(host).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("https://evil.example.com", false)]
    [InlineData("null", false)]
    public void Origin_OnlyLoopbackOrAbsent(string? origin, bool expected)
    {
        LoopbackRequestGuard.IsAllowedOrigin(origin).ShouldBe(expected);
    }
}
