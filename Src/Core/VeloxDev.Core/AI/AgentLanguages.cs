namespace VeloxDev.AI;

public enum AgentLanguages : byte
{
    English = 0,
    ChineseSimplified = 1,
    Chinese = ChineseSimplified,
    ChineseTraditional = 2,
    Japanese = 3,
    Korean = 4,
    French = 5,
    German = 6,
    Spanish = 7,
    Portuguese = 8,
    Russian = 9,
    Arabic = 10,
    Hindi = 11,
    Bengali = 12,
    Urdu = 13,
    Indonesian = 14,
    Malay = 15,
    Vietnamese = 16,
    Thai = 17,
    Turkish = 18,
    Italian = 19,
    Dutch = 20,
    Polish = 21,
    Czech = 22,
    Swedish = 23,
    Danish = 24,
    Norwegian = 25,
    Finnish = 26,
    Greek = 27,
    Hebrew = 28,
    Romanian = 29,
    Hungarian = 30,
    Ukrainian = 31,
    Persian = 32,
}

public static class AgentLanguagesExtensions
{
    private static readonly Dictionary<string, AgentLanguages> LanguageCodeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = AgentLanguages.English,
        ["zh"] = AgentLanguages.ChineseSimplified,
        ["zh-hans"] = AgentLanguages.ChineseSimplified,
        ["zh-cn"] = AgentLanguages.ChineseSimplified,
        ["zh-sg"] = AgentLanguages.ChineseSimplified,
        ["zh-hant"] = AgentLanguages.ChineseTraditional,
        ["zh-tw"] = AgentLanguages.ChineseTraditional,
        ["zh-hk"] = AgentLanguages.ChineseTraditional,
        ["zh-mo"] = AgentLanguages.ChineseTraditional,
        ["ja"] = AgentLanguages.Japanese,
        ["ko"] = AgentLanguages.Korean,
        ["fr"] = AgentLanguages.French,
        ["de"] = AgentLanguages.German,
        ["es"] = AgentLanguages.Spanish,
        ["pt"] = AgentLanguages.Portuguese,
        ["ru"] = AgentLanguages.Russian,
        ["ar"] = AgentLanguages.Arabic,
        ["hi"] = AgentLanguages.Hindi,
        ["bn"] = AgentLanguages.Bengali,
        ["ur"] = AgentLanguages.Urdu,
        ["id"] = AgentLanguages.Indonesian,
        ["ms"] = AgentLanguages.Malay,
        ["vi"] = AgentLanguages.Vietnamese,
        ["th"] = AgentLanguages.Thai,
        ["tr"] = AgentLanguages.Turkish,
        ["it"] = AgentLanguages.Italian,
        ["nl"] = AgentLanguages.Dutch,
        ["pl"] = AgentLanguages.Polish,
        ["cs"] = AgentLanguages.Czech,
        ["sv"] = AgentLanguages.Swedish,
        ["da"] = AgentLanguages.Danish,
        ["no"] = AgentLanguages.Norwegian,
        ["nb"] = AgentLanguages.Norwegian,
        ["nn"] = AgentLanguages.Norwegian,
        ["fi"] = AgentLanguages.Finnish,
        ["el"] = AgentLanguages.Greek,
        ["he"] = AgentLanguages.Hebrew,
        ["ro"] = AgentLanguages.Romanian,
        ["hu"] = AgentLanguages.Hungarian,
        ["uk"] = AgentLanguages.Ukrainian,
        ["fa"] = AgentLanguages.Persian,
    };

    public static string ToLanguageCode(this AgentLanguages language)
    {
        return language switch
        {
            AgentLanguages.English => "en",
            AgentLanguages.Chinese or AgentLanguages.ChineseSimplified => "zh-Hans",
            AgentLanguages.ChineseTraditional => "zh-Hant",
            AgentLanguages.Japanese => "ja",
            AgentLanguages.Korean => "ko",
            AgentLanguages.French => "fr",
            AgentLanguages.German => "de",
            AgentLanguages.Spanish => "es",
            AgentLanguages.Portuguese => "pt",
            AgentLanguages.Russian => "ru",
            AgentLanguages.Arabic => "ar",
            AgentLanguages.Hindi => "hi",
            AgentLanguages.Bengali => "bn",
            AgentLanguages.Urdu => "ur",
            AgentLanguages.Indonesian => "id",
            AgentLanguages.Malay => "ms",
            AgentLanguages.Vietnamese => "vi",
            AgentLanguages.Thai => "th",
            AgentLanguages.Turkish => "tr",
            AgentLanguages.Italian => "it",
            AgentLanguages.Dutch => "nl",
            AgentLanguages.Polish => "pl",
            AgentLanguages.Czech => "cs",
            AgentLanguages.Swedish => "sv",
            AgentLanguages.Danish => "da",
            AgentLanguages.Norwegian => "no",
            AgentLanguages.Finnish => "fi",
            AgentLanguages.Greek => "el",
            AgentLanguages.Hebrew => "he",
            AgentLanguages.Romanian => "ro",
            AgentLanguages.Hungarian => "hu",
            AgentLanguages.Ukrainian => "uk",
            AgentLanguages.Persian => "fa",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }

    public static bool TryParseLanguageCode(string languageCode, out AgentLanguages language)
    {
        language = default;

        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        var normalizedCode = languageCode.Trim().Replace('_', '-');
        if (LanguageCodeMap.TryGetValue(normalizedCode, out language))
        {
            return true;
        }

        var separatorIndex = normalizedCode.IndexOf('-');
        if (separatorIndex > 0)
        {
            return LanguageCodeMap.TryGetValue(normalizedCode.Substring(0, separatorIndex), out language);
        }

        return false;
    }

    public static AgentLanguages ParseLanguageCode(string languageCode)
    {
        if (TryParseLanguageCode(languageCode, out var language))
        {
            return language;
        }

        throw new ArgumentException($"Unsupported language code: {languageCode}", nameof(languageCode));
    }

    public static string GetDisplayName(this AgentLanguages language)
    {
        return language switch
        {
            AgentLanguages.English => "English",
            AgentLanguages.Chinese or AgentLanguages.ChineseSimplified => "Chinese (Simplified)",
            AgentLanguages.ChineseTraditional => "Chinese (Traditional)",
            AgentLanguages.Japanese => "Japanese",
            AgentLanguages.Korean => "Korean",
            AgentLanguages.French => "French",
            AgentLanguages.German => "German",
            AgentLanguages.Spanish => "Spanish",
            AgentLanguages.Portuguese => "Portuguese",
            AgentLanguages.Russian => "Russian",
            AgentLanguages.Arabic => "Arabic",
            AgentLanguages.Hindi => "Hindi",
            AgentLanguages.Bengali => "Bengali",
            AgentLanguages.Urdu => "Urdu",
            AgentLanguages.Indonesian => "Indonesian",
            AgentLanguages.Malay => "Malay",
            AgentLanguages.Vietnamese => "Vietnamese",
            AgentLanguages.Thai => "Thai",
            AgentLanguages.Turkish => "Turkish",
            AgentLanguages.Italian => "Italian",
            AgentLanguages.Dutch => "Dutch",
            AgentLanguages.Polish => "Polish",
            AgentLanguages.Czech => "Czech",
            AgentLanguages.Swedish => "Swedish",
            AgentLanguages.Danish => "Danish",
            AgentLanguages.Norwegian => "Norwegian",
            AgentLanguages.Finnish => "Finnish",
            AgentLanguages.Greek => "Greek",
            AgentLanguages.Hebrew => "Hebrew",
            AgentLanguages.Romanian => "Romanian",
            AgentLanguages.Hungarian => "Hungarian",
            AgentLanguages.Ukrainian => "Ukrainian",
            AgentLanguages.Persian => "Persian",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };
    }
}