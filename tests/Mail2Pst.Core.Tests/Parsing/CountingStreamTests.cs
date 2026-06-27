using System.IO;
using Mail2Pst.Core.Parsing.Mime;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class CountingStreamTests
{
    [Fact]
    public void CopyTo_CountsEveryByte_RetainsNothing()
    {
        var sink = new CountingStream();
        using var src = new MemoryStream(new byte[12345]);
        src.CopyTo(sink);                 // mirrors MimeKit's part.Content.DecodeTo(stream)
        Assert.Equal(12345, sink.BytesWritten);
        Assert.Equal(12345, sink.Length);
    }
}
