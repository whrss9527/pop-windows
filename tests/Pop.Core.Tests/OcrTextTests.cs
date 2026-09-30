using Pop.Core;

namespace Pop.Core.Tests;

public class OcrTextTests
{
    [Fact]
    public void JoinsChineseWithoutSpaces()
    {
        Assert.Equal("今天天气很好", OcrText.JoinLine(["今", "天", "天", "气", "很", "好"]));
        Assert.Equal("你好，世界。", OcrText.JoinLine(["你", "好", "，", "世", "界", "。"]));
    }

    [Fact]
    public void KeepsSpacesBetweenLatinWords()
    {
        Assert.Equal("POP OCR TEST 123", OcrText.JoinLine(["POP", "OCR", "TEST", "123"]));
        Assert.Equal("用React写组件", OcrText.JoinLine(["用", "React", "写", "组", "件"]));
    }

    [Fact]
    public void JoinsLinesAndSkipsEmptyOnes()
    {
        Assert.Equal("第一行\nsecond line", OcrText.Join([["第", "一", "行"], [], ["second", "line"], ["  "]]));
        Assert.Equal("", OcrText.Join([]));
    }
}
