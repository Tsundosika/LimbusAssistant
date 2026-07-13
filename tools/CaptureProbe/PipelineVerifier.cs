using System.Globalization;
using OpenCvSharp;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant.CaptureProbe;

public sealed class PipelineVerifier(WindowsNumberReader reader)
{
    public async Task<int> RunAsync(string imagePath, string dataDirectory)
    {
        using var bgr = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (bgr.Empty())
        {
            Console.WriteLine($"could not read image: {imagePath}");
            return 1;
        }
        using var mat = new Mat();
        Cv2.CvtColor(bgr, mat, ColorConversionCodes.BGR2BGRA);
        var pixels = new byte[mat.Width * mat.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(mat.Data, pixels, 0, pixels.Length);
        var frame = new CaptureFrame(pixels, mat.Width, mat.Height);
        var content = LetterboxDetector.DetectContent(frame);

        var data = GameData.Load(dataDirectory);
        Console.WriteLine($"dataset: {data.Identities.Count} identities, {data.Enemies.Count} enemies");

        var candidates = RibbonScanner.FindSkillRibbons(mat, content);
        Console.WriteLine($"ribbon candidates: {candidates.Count}");
        var readings = new List<(PixelRect Rect, string Text)>();
        foreach (var candidate in candidates)
        {
            var text = await reader.ReadTextAsync(mat, candidate);
            if (text.Text.Count(char.IsLetter) >= 4 && !BannerWords.IsNonSkillBanner(text.Text))
            {
                readings.Add((candidate, text.Text));
                Console.WriteLine($"  banner at {candidate.X},{candidate.Y}: \"{text.Text}\"");
            }
        }
        if (readings.Count < 2)
        {
            Console.WriteLine("VERIFY FAIL: need two readable banners for a clash pair");
            return 1;
        }
        var ordered = readings.OrderBy(reading => reading.Rect.X).ToList();

        (SkillData Skill, IdentityData Identity)? ally = null;
        (SkillData Skill, EnemyData Owner)? enemy = null;
        foreach (var (_, text) in ordered)
        {
            ally ??= MatchIdentity(data, text);
        }
        foreach (var (_, text) in ordered)
        {
            var candidateEnemy = MatchEnemy(data, text);
            if (candidateEnemy is not null && candidateEnemy.Value.Skill.Name != ally?.Skill.Name)
            {
                enemy = candidateEnemy;
            }
        }
        if (ally is null || enemy is null)
        {
            Console.WriteLine($"VERIFY FAIL: ally {(ally is null ? "not matched" : ally.Value.Skill.Name)}, enemy {(enemy is null ? "not matched" : enemy.Value.Skill.Name)}");
            return 1;
        }
        Console.WriteLine($"your attack:  {ally.Value.Skill.Name} ({ally.Value.Identity.Name})");
        Console.WriteLine($"enemy attack: {enemy.Value.Skill.Name} ({enemy.Value.Owner.Name})");

        var solver = new TurnSolver();
        var unit = new TurnUnit(ally.Value.Identity, 0);
        var result = solver.EvaluateClash(unit, ally.Value.Skill, new EnemyThreat(enemy.Value.Owner, enemy.Value.Skill));
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"win probability: {result.WinProbability:P1} · deal {result.ExpectedDamageDealt:F1} · take {result.ExpectedDamageTaken:F1}"));
        var plausible = result.WinProbability is >= 0 and <= 1 && !double.IsNaN(result.WinProbability);
        Console.WriteLine(plausible ? "VERIFY PASS" : "VERIFY FAIL: degenerate probability");
        return plausible ? 0 : 1;
    }

    static (SkillData Skill, IdentityData Identity)? MatchIdentity(GameData data, string text)
    {
        var normalized = Normalize(text);
        foreach (var identity in data.Identities)
        {
            foreach (var skill in identity.Skills)
            {
                var candidate = Normalize(skill.Name);
                if (candidate.Length >= 4 && (normalized == candidate || normalized.Contains(candidate, StringComparison.Ordinal)))
                {
                    return (skill, identity);
                }
            }
        }
        return null;
    }

    static (SkillData Skill, EnemyData Owner)? MatchEnemy(GameData data, string text)
    {
        var normalized = Normalize(text);
        foreach (var enemy in data.Enemies)
        {
            foreach (var skill in enemy.Skills)
            {
                var candidate = Normalize(skill.Name);
                if (candidate.Length >= 4 && (normalized == candidate || normalized.Contains(candidate, StringComparison.Ordinal)))
                {
                    return (skill, enemy);
                }
            }
        }
        return null;
    }

    static string Normalize(string text) =>
        new(text.ToLowerInvariant().Where(char.IsAsciiLetter).ToArray());
}
