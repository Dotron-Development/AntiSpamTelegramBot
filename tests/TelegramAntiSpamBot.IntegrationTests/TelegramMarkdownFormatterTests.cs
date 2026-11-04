namespace TelegramAntiSpamBot.IntegrationTests;

public class TelegramMarkdownFormatterTests
{
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
