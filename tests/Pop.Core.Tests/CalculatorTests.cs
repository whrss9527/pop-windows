using Pop.Core;

namespace Pop.Core.Tests;

public class CalculatorTests
{
    [Fact]
    public void PrecedenceAndParentheses()
    {
        Assert.Equal(7, Calculator.Evaluate("1+2*3"));
        Assert.Equal(9, Calculator.Evaluate("(1+2)*3"));
        Assert.Equal(2.5, Calculator.Evaluate("10 / 4"));
        Assert.Equal(1.25, Calculator.Evaluate("(3.5 - 1) / 2"));
    }

    [Fact]
    public void PowerIsRightAssociativeAndBindsTighterThanUnaryMinus()
    {
        Assert.Equal(512, Calculator.Evaluate("2^3^2"));
        Assert.Equal(-4, Calculator.Evaluate("-2^2"));
        Assert.Equal(0.5, Calculator.Evaluate("2^-1"));
    }

    [Fact]
    public void PercentAndFullWidthSymbols()
    {
        Assert.Equal(30, Calculator.Evaluate("200*15%"));
        Assert.Equal(6, Calculator.Evaluate("3 × 4 ÷ 2"));
        Assert.Equal(9, Calculator.Evaluate("（1＋2）×3"));
        Assert.Equal(3500, Calculator.Evaluate("1,000 + 2,500"));
        Assert.Equal(3, Calculator.Evaluate("1+2="));
    }

    [Fact]
    public void InvalidInputReturnsNull()
    {
        Assert.Null(Calculator.Evaluate("1/0"));
        Assert.Null(Calculator.Evaluate("1+"));
        Assert.Null(Calculator.Evaluate("abc"));
        Assert.Null(Calculator.Evaluate("(1+2"));
        Assert.Null(Calculator.Evaluate(""));
        Assert.Null(Calculator.Evaluate(new string('(', 200) + "1"));
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("7", Calculator.Format(7));
        Assert.Equal("-3", Calculator.Format(-3));
        Assert.Equal("2.5", Calculator.Format(2.5));
        Assert.Equal("0.3", Calculator.Format(0.1 + 0.2));
        Assert.Equal("1e+20", Calculator.Format(1e20));
    }

    [Fact]
    public void NormalizeUnifiesSymbols()
    {
        Assert.Equal("(1+2)*3/4-5%", Calculator.Normalize("（1＋2）× 3 ÷ 4 － 5％ ＝"));
        Assert.Equal("1000+2500", Calculator.Normalize("1,000 + 2，500=="));
    }
}
