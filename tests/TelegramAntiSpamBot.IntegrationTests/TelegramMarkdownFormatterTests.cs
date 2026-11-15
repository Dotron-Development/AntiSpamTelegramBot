namespace TelegramAntiSpamBot.IntegrationTests;

public class TelegramMarkdownFormatterTests
{
    [Fact]
    public void MarkdownLinkPreservesUrlAndEscapesItsClosingParenthesis()
    {
        Assert.Equal("Fact\\. [Source](https://example.com/a_b_c?q=(one\\))",
            TelegramMarkdownFormatter.Format("Fact. [Source](https://example.com/a_b_c?q=(one))"));
    }

    [Theory]
    [InlineData("[Source](https://example.com/a\\(b\\))", "[Source](https://example.com/a(b\\))")]
    [InlineData("[A \\[B\\] *C*](https://example.com/)", "[A \\[B\\] \\*C\\*](https://example.com/)")]
    [InlineData("[Source](ftp://example.com/file)", "\\[Source\\]\\(ftp://example\\.com/file\\)")]
    [InlineData("[Source](https://example.com/unclosed", "\\[Source\\]\\(https://example\\.com/unclosed")]
    [InlineData("`[Source](https://example.com/)`", "`[Source](https://example.com/)`")]
    public void MarkdownLinksEscapeLabelsAndTargetsAndKeepUnsupportedOrCodeLinksLiteral(string input, string expected)
    {
        Assert.Equal(expected, TelegramMarkdownFormatter.Format(input));
    }

    [Fact]
    public void SingleMessageDoesNotCutAnInlineCitationAtLimit()
    {
        var prefix = new string('a', 4080) + " ";
        Assert.Equal(prefix, TelegramMarkdownFormatter.Format(prefix + "[Source](https://example.com/source)"));
    }

    [Fact]
    public void MultipartEscapingPreservesAllTextAndUnicode()
    {
        var text = new string('!', 2047) + "😀" + new string('!', 3000);
        var parts = TelegramMarkdownFormatter.FormatParts(text);
        Assert.True(parts.Count > 1);
        Assert.All(parts, part =>
        {
            Assert.InRange(part.Length, 1, 4096);
            Assert.DoesNotContain('\uFFFD', Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(part)));
        });
        Assert.Equal(text.Replace("!", "\\!"), string.Concat(parts));
    }

    [Theory]
    [InlineData("**", "*", "*")]
    [InlineData("_", "_", "_")]
    [InlineData("*", "_", "_")]
    [InlineData("`", "`", "`")]
    [InlineData("```cs\n", "```cs\n", "```")]
    public void MultipartFormattingReopensAndClosesEntities(string opening, string prefix, string closing)
    {
        var content = new string('a', 10000);
        var marker = opening.StartsWith("```") ? "```" : opening;
        var parts = TelegramMarkdownFormatter.FormatParts(opening + content + marker);
        Assert.Equal(3, parts.Count);
        Assert.All(parts, part =>
        {
            Assert.InRange(part.Length, 1, 4096);
            Assert.StartsWith(prefix, part);
            Assert.EndsWith(closing, part);
        });
        Assert.Equal(content, string.Concat(parts.Select(part => part[prefix.Length..^closing.Length])));
    }

    [Fact]
    public void MultipartMovesWholeUrlToNextMessage()
    {
        const string url = "https://example.com/a_b?q=one-two";
        var text = new string('a', 4080) + "\n" + url + "\ntail";
        var parts = TelegramMarkdownFormatter.FormatParts(text);
        Assert.Equal(2, parts.Count);
        Assert.StartsWith(TelegramMarkdownFormatter.Format(url), parts[1]);
        Assert.Equal(new string('a', 4080) + "\n" + TelegramMarkdownFormatter.Format(url) + "\ntail", string.Concat(parts));
    }

    [Fact]
    public void MultipartNearFullMessagePreservesFollowingEntityAndUnclosedMarkers()
    {
        var prefix = new string('a', 4095);
        var parts = TelegramMarkdownFormatter.FormatParts(prefix + "**😀**tail `unclosed!");
        Assert.Equal(new[] { prefix, "*😀*tail \\`unclosed\\!" }, parts);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Обычный текст\nSecond line", "Обычный текст\nSecond line")]
    [InlineData("**bold**", "*bold*")]
    [InlineData("_italic_", "_italic_")]
    [InlineData("*italic*", "_italic_")]
    [InlineData("**bold** _italic_ `code` tail", "*bold* _italic_ `code` tail")]
    [InlineData("**a.b!**", "*a\\.b\\!*")]
    [InlineData("_a+b_", "_a\\+b_")]
    [InlineData("`a_b * [x] (y) !`", "`a_b * [x] (y) !`")]
    [InlineData("`C:\\temp`", "`C:\\\\temp`")]
    [InlineData("```cs\nvar x = 1;\n```", "```cs\nvar x = 1;\n```")]
    [InlineData("```c++\r\na_b();\r\n```", "```c++\na_b();\r\n```")]
    [InlineData("```objective-c\nx\n```", "```objective-c\nx\n```")]
    [InlineData("```my_lang\nx\n```", "```my_lang\nx\n```")]
    [InlineData("```\na_b\n```", "```\na_b\n```")]
    [InlineData("```a_b```", "```\na_b```")]
    [InlineData("```not a language\nx```", "```\nnot a language\nx```")]
    [InlineData("```русский\nx```", "```\nрусский\nx```")]
    [InlineData("```\n`x` C:\\temp\n```", "```\n\\`x\\` C:\\\\temp\n```")]
    [InlineData("Unclosed **bold!", "Unclosed \\*\\*bold\\!")]
    [InlineData("Unclosed _italic", "Unclosed \\_italic")]
    [InlineData("Unclosed `code", "Unclosed \\`code")]
    [InlineData("Unclosed ```code", "Unclosed \\`\\`\\`code")]
    [InlineData("__", "\\_\\_")]
    [InlineData("``", "\\`\\`")]
    [InlineData("😀 **😀** `😀`", "😀 *😀* `😀`")]
    public void ConvertsSupportedMarkdownAndPreservesLiteralText(string input, string expected)
    {
        Assert.Equal(expected, TelegramMarkdownFormatter.Format(input));
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('_')]
    [InlineData('*')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData('~')]
    [InlineData('`')]
    [InlineData('>')]
    [InlineData('#')]
    [InlineData('+')]
    [InlineData('-')]
    [InlineData('=')]
    [InlineData('|')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('.')]
    [InlineData('!')]
    public void EscapesEveryTelegramReservedCharacter(char character)
    {
        Assert.Equal("\\" + character, TelegramMarkdownFormatter.Format(character.ToString()));
    }

    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(10000)]
    public void CapsPlainTextAtTelegramLimit(int length)
    {
        Assert.Equal(new string('a', Math.Min(length, 4096)), TelegramMarkdownFormatter.Format(new string('a', length)));
    }

    [Fact]
    public void CapsEscapedTextWithoutLeavingTrailingEscape()
    {
        Assert.Equal(string.Concat(Enumerable.Repeat("\\!", 2048)), TelegramMarkdownFormatter.Format(new string('!', 5000)));
        var prefix = new string('a', 4095);
        Assert.Equal(prefix, TelegramMarkdownFormatter.Format(prefix + "!"));
    }

    [Theory]
    [InlineData("**", "*", "*")]
    [InlineData("_", "_", "_")]
    [InlineData("*", "_", "_")]
    [InlineData("`", "`", "`")]
    [InlineData("```", "```\n", "```")]
    public void TruncatesFormattedContentAndKeepsClosingMarkers(string marker, string opening, string closing)
    {
        var expected = opening + new string('a', 4096 - opening.Length - closing.Length) + closing;
        Assert.Equal(expected, TelegramMarkdownFormatter.Format(marker + new string('a', 5000) + marker));
    }

    [Fact]
    public void EscapedFormattedContentReservesClosingMarker()
    {
        var expected = "*" + string.Concat(Enumerable.Repeat("\\!", 2047)) + "*";
        Assert.Equal(expected, TelegramMarkdownFormatter.Format("**" + new string('!', 3000) + "**"));
    }

    [Theory]
    [InlineData(4094, true)]
    [InlineData(4095, false)]
    public void NeverSplitsUnicodePairInPlainText(int prefixLength, bool fits)
    {
        var prefix = new string('a', prefixLength);
        Assert.Equal(prefix + (fits ? "😀" : ""), TelegramMarkdownFormatter.Format(prefix + "😀tail"));
    }

    [Theory]
    [InlineData(4092, true)]
    [InlineData(4093, false)]
    public void NeverSplitsUnicodePairOrProducesEmptyEntityAtLimit(int prefixLength, bool fits)
    {
        var prefix = new string('a', prefixLength);
        Assert.Equal(prefix + (fits ? "*😀*" : ""), TelegramMarkdownFormatter.Format(prefix + "**😀**tail"));
    }

    [Fact]
    public void DoesNotOpenFormattingWithoutSpaceForContentAndClosingMarker()
    {
        var prefix = new string('a', 4094);
        Assert.Equal(prefix, TelegramMarkdownFormatter.Format(prefix + "**x**tail"));
    }

    [Fact]
    public void EscapedTextFollowedByUnicodeRespectsCombinedBudget()
    {
        var prefix = string.Concat(Enumerable.Repeat("\\!", 2047));
        Assert.Equal(prefix + "😀", TelegramMarkdownFormatter.Format(new string('!', 2047) + "😀!"));
    }
}
