// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Mail2Pst.Core.Calendar;
using Xunit;

namespace Mail2Pst.Core.Tests.Calendar;

public class ParseAttachmentTests
{
    [Fact]
    public void Remote_uri_is_a_link()
    {
        var a = ICalTextParser.ParseAttachment("ATTACH;FILENAME=\"r.txt\";FMTTYPE=text/plain:https://example.com/r").Value!;
        Assert.Equal("https://example.com/r", a.Uri);
        Assert.Null(a.InlineData);
        Assert.Equal("r.txt", a.FileName);
    }

    [Fact]
    public void Inline_base64_binary_is_decoded()
    {
        var a = ICalTextParser.ParseAttachment("ATTACH;ENCODING=BASE64;VALUE=BINARY;FMTTYPE=text/plain:aGk=").Value!;
        Assert.Null(a.Uri);
        Assert.Equal("hi", Encoding.UTF8.GetString(a.InlineData!));
    }

    [Fact]
    public void Data_uri_is_decoded_to_inline()
    {
        var a = ICalTextParser.ParseAttachment("ATTACH;FILENAME=\"n.txt\":data:text/plain;base64,aGk=").Value!;
        Assert.Null(a.Uri);
        Assert.Equal("hi", Encoding.UTF8.GetString(a.InlineData!));
        Assert.Equal("n.txt", a.FileName);
        Assert.Equal("text/plain", a.FormatType);
    }

    [Fact]
    public void Data_uri_without_base64_is_decoded_as_utf8()
    {
        var a = ICalTextParser.ParseAttachment("ATTACH:data:text/plain,hello").Value!;
        Assert.Null(a.Uri);
        Assert.Equal("hello", Encoding.UTF8.GetString(a.InlineData!));
    }

    [Fact]
    public void Malformed_attach_returns_null_value_and_warning()
    {
        var r = ICalTextParser.ParseAttachment("ATTACH;:::"); // must not throw
        Assert.Null(r.Value);
        Assert.NotEmpty(r.Warnings);
    }
}
