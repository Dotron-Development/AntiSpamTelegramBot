
namespace TelegramAntiSpamBot.Persistence;

public static class MessageContentSanitizer
{
    public static string Sanitize(string message)
    {
        var normalized = message.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(normalized.Length);
        var inNumber = false;

        for (var index = 0; index < normalized.Length;)
        {
            var rune = Rune.GetRuneAt(normalized, index);
            index += rune.Utf16SequenceLength;
            var category = Rune.GetUnicodeCategory(rune);

            // Keycap emoji can start with a digit, # or *.
            var keycapIndex = index;
            if (keycapIndex < normalized.Length && normalized[keycapIndex] == '\uFE0F')
                keycapIndex++;
            if ((Rune.IsDigit(rune) || rune.Value is '#' or '*')
                && keycapIndex < normalized.Length && normalized[keycapIndex] == '\u20E3')
            {
                index = keycapIndex + 1;
                continue;
            }

            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.Format or UnicodeCategory.Control
                || IsEmoji(rune.Value) || rune.Value is >= 0xFE00 and <= 0xFE0F
                or >= 0xE0100 and <= 0xE01EF)
                continue;

            if (category == UnicodeCategory.DecimalDigitNumber)
            {
                if (!inNumber)
                    result.Append('х');
                inNumber = true;
                continue;
            }

            inNumber = false;
            result.Append(Rune.ToLowerInvariant(rune).ToString());
        }

        return result.ToString();
    }

    private static bool IsEmoji(int value) => value is
        >= 0x1F000 and <= 0x1FAFF
        or >= 0x2600 and <= 0x27BF
        or >= 0x2194 and <= 0x2199
        or >= 0x21A9 and <= 0x21AA
        or >= 0x231A and <= 0x231B
        or >= 0x23E9 and <= 0x23F3
        or >= 0x23F8 and <= 0x23FA
        or >= 0x25AA and <= 0x25AB
        or >= 0x25FB and <= 0x25FE
        or 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139
        or 0x2328 or 0x23CF or 0x24C2 or 0x25B6 or 0x25C0
        or 0x2934 or 0x2935 or 0x2B05 or 0x2B06 or 0x2B07
        or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 or 0x3030 or 0x303D
        or 0x3297 or 0x3299;
}
