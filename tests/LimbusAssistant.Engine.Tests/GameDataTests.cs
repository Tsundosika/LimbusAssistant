using Tsundosika.LimbusAssistant.Engine;

namespace Tsundosika.LimbusAssistant.Engine.Tests;

public class GameDataTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), $"limbus-assistant-tests-{Guid.NewGuid():N}");

    public GameDataTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void LoadsIdentitiesAndEnemiesFromJson()
    {
        File.WriteAllText(Path.Combine(_directory, "identities.json"), """
        [
          {
            "id": "test-identity",
            "name": "Test Identity",
            "sinner": "Tester",
            "defenseLevel": 40,
            "skills": [
              {
                "id": "test-s1",
                "name": "Test Skill",
                "basePower": 4,
                "coinPower": 7,
                "coinCount": 1,
                "sin": "Gloom",
                "damageType": "Slash",
                "offenseLevel": 40
              }
            ]
          }
        ]
        """);
        File.WriteAllText(Path.Combine(_directory, "enemies.json"), """
        [
          {
            "id": "test-enemy",
            "name": "Test Enemy",
            "defenseLevel": 35,
            "staggerThreshold": 50,
            "resistances": {
              "physical": { "Slash": 1.5 },
              "sin": { "Gloom": 0.75 }
            },
            "skills": []
          }
        ]
        """);

        var data = GameData.Load(_directory);

        var identity = Assert.Single(data.Identities);
        Assert.Equal("Test Identity", identity.Name);
        var skill = Assert.Single(identity.Skills);
        Assert.Equal(SinType.Gloom, skill.Sin);
        Assert.Equal(DamageType.Slash, skill.DamageType);
        var enemy = Assert.Single(data.Enemies);
        Assert.Equal(1.5, enemy.Resistances.PhysicalFor(DamageType.Slash));
        Assert.Equal(0.75, enemy.Resistances.SinFor(SinType.Gloom));
        Assert.Equal(1.0, enemy.Resistances.SinFor(SinType.Wrath));
    }

    [Fact]
    public void MissingFilesYieldEmptyData()
    {
        var data = GameData.Load(Path.Combine(_directory, "missing"));
        Assert.Empty(data.Identities);
        Assert.Empty(data.Enemies);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }
}
