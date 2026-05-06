namespace VeloxDev.Docs.Translation;

/// <summary>
/// Snapshot of translation progress reported after each completed <see cref="WikiTranslationJob"/>.
/// </summary>
/// <param name="Completed">Number of jobs completed so far (including this one).</param>
/// <param name="Total">Total number of jobs in this run.</param>
/// <param name="LastJob">The job that was just finished.</param>
public sealed record WikiTranslationProgress(int Completed, int Total, WikiTranslationJob LastJob)
{
    /// <summary>Completion ratio in [0, 1].</summary>
    public double Fraction => Total == 0 ? 1.0 : (double)Completed / Total;
}
