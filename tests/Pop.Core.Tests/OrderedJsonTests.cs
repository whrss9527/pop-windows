using Pop.Core;

namespace Pop.Core.Tests;

public class OrderedJsonTests
{
    [Fact]
    public void KeepsOrderAndNumbers()
    {
        const string input = "{\"b\": 1, \"a\": [1.50, -2e3, true, null], \"s\": \"\\u6548\\ud83d\\ude00\\n\\\"x\\\"\"}";
        var value = OrderedJson.Parse(input);
        Assert.NotNull(value);
        const string pretty =
            "{\n" +
            "  \"b\": 1,\n" +
            "  \"a\": [\n" +
            "    1.50,\n" +
            "    -2e3,\n" +
            "    true,\n" +
            "    null\n" +
            "  ],\n" +
            "  \"s\": \"效😀\\n\\\"x\\\"\"\n" +
            "}";
        Assert.Equal(pretty, OrderedJson.Format(value!));
        Assert.Equal("{\"b\":1,\"a\":[1.50,-2e3,true,null],\"s\":\"效😀\\n\\\"x\\\"\"}", OrderedJson.Compact(value!));
        Assert.Equal(pretty, OrderedJson.Format(input));
        Assert.Null(OrderedJson.Parse("{\"a\": 1,}"));
        Assert.Null(OrderedJson.Parse("{'a': 1}"));
        Assert.Null(OrderedJson.Parse("{\"a\": 1}x"));
    }

    [Fact]
    public void ConvenienceFunctions()
    {
        Assert.Equal("{\n  \"b\": 1,\n  \"a\": [\n    1,\n    2\n  ]\n}", OrderedJson.Format("{\"b\":1,\"a\":[1,2]}"));
        Assert.Equal("{\"b\":1,\"a\":{},\"c\":[]}", OrderedJson.Compact(" {\"b\" : 1, \"a\": { }, \"c\": [ ] } "));
        Assert.Equal("{\n  \"e\": {},\n  \"n\": []\n}", OrderedJson.Format("{\"e\":{},\"n\":[]}"));
        Assert.Null(OrderedJson.Format("{"));
        Assert.Null(OrderedJson.Compact("tru"));
        Assert.Null(OrderedJson.Compact(""));
        Assert.Equal("\"\\u007f\\u0001/\"", OrderedJson.Compact("\"\\u007F\\u0001\\/\""));
    }

    [Fact]
    public void RejectsBadStringsAndNumbers()
    {
        Assert.Null(OrderedJson.Parse("\"\\ud83d\""));
        Assert.Null(OrderedJson.Parse("\"\\ude00\""));
        Assert.Null(OrderedJson.Parse("\"a\nb\""));
        Assert.Null(OrderedJson.Parse("1."));
        Assert.Null(OrderedJson.Parse("-"));
        Assert.Null(OrderedJson.Parse(new string('[', 600) + new string(']', 600)));
        Assert.NotNull(OrderedJson.Parse(new string('[', 500) + new string(']', 500)));
    }

    [Fact]
    public void Equality()
    {
        Assert.Equal(OrderedJson.Parse("{\"a\":[1,\"x\",null]}"), OrderedJson.Parse("{ \"a\" : [ 1 , \"x\" , null ] }"));
        Assert.NotEqual(OrderedJson.Parse("{\"a\":1,\"b\":2}"), OrderedJson.Parse("{\"b\":2,\"a\":1}"));
    }

    [Fact]
    public void Rows()
    {
        var rows = OrderedJson.Rows("{\"b\":1,\"a\":2}");
        Assert.Equal(["JSON 格式化", "压缩版"], rows.Select(r => r.Label).ToArray());
        Assert.Equal("{\n  \"b\": 1,\n  \"a\": 2\n}", rows[0].Value);
        Assert.Equal("{\"b\":1,\"a\":2}", rows[1].Value);
        Assert.Single(OrderedJson.Rows("[]"));
        Assert.Empty(OrderedJson.Rows("{nope}"));
    }
}
