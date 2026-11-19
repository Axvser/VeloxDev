using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentLanguagesTests
{
    [TestMethod]
    public void Chinese_IsSameAs_ChineseSimplified()
    {
        Assert.AreEqual(AgentLanguages.ChineseSimplified, AgentLanguages.Chinese);
    }

    [TestMethod]
    public void TryParseLanguageCode_ValidCode_ReturnsTrue()
    {
        Assert.IsTrue(AgentLanguagesExtensions.TryParseLanguageCode("en", out var lang));
        Assert.AreEqual(AgentLanguages.English, lang);
    }

    [TestMethod]
    public void TryParseLanguageCode_ChineseVariants_MapCorrectly()
    {
        Assert.IsTrue(AgentLanguagesExtensions.TryParseLanguageCode("zh", out var zh));
        Assert.AreEqual(AgentLanguages.ChineseSimplified, zh);

        Assert.IsTrue(AgentLanguagesExtensions.TryParseLanguageCode("zh-hant", out var zhTw));
        Assert.AreEqual(AgentLanguages.ChineseTraditional, zhTw);
    }

    [TestMethod]
    public void TryParseLanguageCode_CaseInsensitive()
    {
        Assert.IsTrue(AgentLanguagesExtensions.TryParseLanguageCode("EN", out var lang));
        Assert.AreEqual(AgentLanguages.English, lang);
    }

    [TestMethod]
    public void TryParseLanguageCode_Unknown_ReturnsFalse()
    {
        Assert.IsFalse(AgentLanguagesExtensions.TryParseLanguageCode("xx-fake", out _));
    }

    [TestMethod]
    public void ToLanguageCode_English_ReturnsEn()
    {
        Assert.AreEqual("en", AgentLanguages.English.ToLanguageCode());
    }

    [TestMethod]
    public void ToLanguageCode_Chinese_ReturnsZhHans()
    {
        Assert.AreEqual("zh-Hans", AgentLanguages.Chinese.ToLanguageCode());
    }
}
