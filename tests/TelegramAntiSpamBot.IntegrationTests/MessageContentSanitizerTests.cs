using System.Security.Cryptography;
using System.Text;
using TelegramAntiSpamBot.Persistence;

namespace TelegramAntiSpamBot.IntegrationTests;

public class MessageContentSanitizerTests
{
    [Theory]
    [InlineData("Предлагаю 1000 рублей доход", "предлагаюхрублейдоход")]
    [InlineData("Предлагаю 10000 рублей доход", "предлагаюхрублейдоход")]
    [InlineData("INCOME ١٢٣", "incomeх")]
    [InlineData("收入 १२३", "收入х")]
    [InlineData("収入 １２３", "収入х")]
    [InlineData("수입 𝟙𝟚𝟛", "수입х")]
    [InlineData("Cafe\u0301", "café")]
    [InlineData("ＦＯＯ", "foo")]
    [InlineData("a \t\r\n\u00A0\u200Bb", "ab")]
    [InlineData("a\0\u2060\u202Eb", "ab")]
    [InlineData("доход 1 🔥 000", "доходх")]
    [InlineData("a🔥👩🏽‍💻🇯🇵❤️☀️b", "ab")]
    [InlineData("a1️⃣#️⃣*️⃣b", "ab")]
    [InlineData("नमस्ते مرحبًا 你好", "नमस्तेمرحبًا你好")]
    [InlineData("Offer 100, price 200!", "offerх,priceх!")]
    [InlineData("работа раб0та", "работарабхта")]
    [InlineData(" \u200B🔥❤️", "")]
    [InlineData("", "")]
    public void SanitizesContent(string original, string expected)
    {
        Assert.Equal(expected, MessageContentSanitizer.Sanitize(original));
    }

    [Theory]
    [InlineData("предлагаю 1000 рублей доход", "ПРЕДЛАГАЮ 🔥 10001 рублей\nдоход")]
    [InlineData("income 123", "i n c o m e 👩‍💻 ١٢٣٤٥")]
    [InlineData("收入 123", "收\u200B入 🇨🇳 १२३४")]
    public void VariantsHaveTheSameHash(string original, string variant)
    {
        Assert.Equal(Hash(original), Hash(variant));
    }

    [Fact]
    public void DifferentTextHasDifferentHashes()
    {
        Assert.NotEqual(Hash("предлагаю доход"), Hash("ищу работу"));
        Assert.NotEqual(Hash("offer!"), Hash("offer?"));
        Assert.NotEqual(Hash("работа"), Hash("раб0та"));
    }

    [Theory]
    [InlineData("Предлагаю 1000🔥 рублей")]
    [InlineData("नमस्ते ١٢٣")]
    public void SanitizationIsIdempotent(string original)
    {
        var sanitized = MessageContentSanitizer.Sanitize(original);
        Assert.Equal(sanitized, MessageContentSanitizer.Sanitize(sanitized));
    }

    private static string Hash(string content) => Convert.ToHexString(
        SHA256.HashData(Encoding.Unicode.GetBytes(MessageContentSanitizer.Sanitize(content))));
}
