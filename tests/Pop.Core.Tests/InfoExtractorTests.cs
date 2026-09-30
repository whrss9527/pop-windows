using Pop.Core;

namespace Pop.Core.Tests;

public class InfoExtractorTests
{
    [Fact]
    public void FindsEachKindInOrderWithoutDuplicates()
    {
        var text = "联系 pop@example.com，电话 138-1234-5678；下载 https://github.com/whrss9527/pop/releases，"
            + "文档在 www.example.com；测试机 192.168.1.20:8080，备用 pop@example.com";
        var items = InfoExtractor.Extract(text);
        Assert.Equal(["pop@example.com", "138-1234-5678", "https://github.com/whrss9527/pop/releases",
                      "www.example.com", "192.168.1.20:8080"], items.Select(i => i.Value));
        Assert.Equal([InfoKind.Email, InfoKind.Phone, InfoKind.Link, InfoKind.Link, InfoKind.Ip], items.Select(i => i.Kind));

        var card = InfoExtractor.Card(items);
        Assert.Equal("提取信息", card.Title);
        Assert.Equal(["邮箱 1", "电话 1", "链接 1", "链接 2", "IP 地址 1"], card.Lines.Select(l => l.Label));
        Assert.Equal("https://github.com/whrss9527/pop/releases\nwww.example.com",
            InfoExtractor.CopyAllActions(items).First(a => a.Label == "复制全部链接").Value);
        // 没写协议的链接按 https 打开
        Assert.Equal(["https://github.com/whrss9527/pop/releases", "https://www.example.com"], InfoExtractor.LinksToOpen(items));
        Assert.Equal("2 个链接，1 个邮箱，1 个电话，1 个 IP 地址", card.Body);
    }

    [Fact]
    public void PhoneFormats()
    {
        var text = "客服 400-123-4567，北京 010-12345678，国际 +1 (415) 555-2671，手机 +86 13812345678";
        Assert.Equal(["400-123-4567", "010-12345678", "+1 (415) 555-2671", "+86 13812345678"],
            InfoExtractor.Extract(text).Select(i => i.Value));
        // 长数字串里的一段不算手机号
        Assert.Empty(InfoExtractor.Extract("订单号 202609291381234567890"));
    }

    [Fact]
    public void FileNamesAreNotLinks()
    {
        Assert.Empty(InfoExtractor.Extract("打开 main.py 和 README.md，再看 Pop.app"));
    }

    [Fact]
    public void LinkBoundaries()
    {
        // 句末标点、多出来的右括号不算网址的一部分
        Assert.Equal(["https://example.com/a"], InfoExtractor.Extract("看这里 https://example.com/a.").Select(i => i.Value));
        Assert.Equal(["https://example.com/a"], InfoExtractor.Extract("（见 https://example.com/a)").Select(i => i.Value));
        Assert.Equal(["www.example.com"], InfoExtractor.Extract("见www.example.com").Select(i => i.Value));
        // 带协议的纯 IP 地址算链接，不带的算 IP
        Assert.Equal([InfoKind.Link], InfoExtractor.Extract("http://10.0.0.1/admin").Select(i => i.Kind));
    }

    [Fact]
    public void LongListIsTruncated()
    {
        var items = Enumerable.Range(1, 3).Select(i => new InfoItem(InfoKind.Email, $"u{i}@example.com")).ToList();
        var card = InfoExtractor.Card(items, limit: 2);
        Assert.Equal(2, card.Lines.Count);
        Assert.Equal("3 个邮箱（只列出前 2 项，复制全部时包括所有的）", card.Body);
        Assert.Equal("u1@example.com\nu2@example.com\nu3@example.com", InfoExtractor.CopyAllActions(items)[0].Value);
        Assert.Empty(InfoExtractor.LinksToOpen(items));
    }

    [Fact]
    public void WhenToOffer()
    {
        Assert.False(InfoExtractor.IsWorthExtracting("https://example.com"));
        Assert.True(InfoExtractor.IsWorthExtracting("见 https://example.com"));
        Assert.True(InfoExtractor.IsWorthExtracting("a@example.com b@example.com"));
        Assert.False(InfoExtractor.IsWorthExtracting("今天天气很好"));
    }
}
