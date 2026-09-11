namespace VeloxDev.SamplerTest;

/// <summary>
/// Runs every registered sampler at several eased times — including ones outside the unit interval — and compares
/// what it wrote against the closed form its entry states.
/// </summary>
[TestClass]
public class SamplerConformanceTests
{
    [TestMethod]
    public void EverySampler_MatchesItsClosedForm_AtEveryTime()
    {
        // 收集全部不符再一次性报出：一个采样器错在哪一行比"第一个失败就停"有用得多。
        var failures = new List<string>();

        foreach (var entry in SamplerRegistry.Entries)
        {
            var sampler = entry.Instantiate();

            foreach (var t in SamplerEntry.Times)
            {
                var expected = entry.Expected(t);
                var actual = entry.Write(sampler, t);

                if (!entry.Equivalent(expected, actual))
                {
                    failures.Add($"{entry}（规则 {entry.Rule}）t={t}：期望 {expected}，实际 {actual}");
                }
            }
        }

        CollectionAssert.AreEqual(
            Array.Empty<string>(),
            failures.ToArray(),
            $"{failures.Count} 处与闭式解不符：{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [TestMethod]
    public void EveryRule_IsExercised()
    {
        // 三条规则都要有代表，否则"按规则验证"这句话里有一条从未被跑过。
        var covered = SamplerRegistry.Entries.Select(entry => entry.Rule).Distinct().ToList();

        CollectionAssert.AreEquivalent(Enum.GetValues<SamplerRule>(), covered,
            $"注册表里没有覆盖到的规则：{string.Join(", ", Enum.GetValues<SamplerRule>().Except(covered))}");
    }
}
