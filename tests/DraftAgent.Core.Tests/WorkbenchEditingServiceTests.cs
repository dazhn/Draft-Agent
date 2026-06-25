using DraftAgent.Core;

namespace DraftAgent.Core.Tests;

public sealed class WorkbenchEditingServiceTests
{
    [Fact]
    public void AddCharacterIgnoresActionFragmentName()
    {
        var service = new WorkbenchEditingService(new InMemoryWorkflowLogger());
        var project = EmptyProject();

        var result = service.AddCharacter(project, "喘着气");

        Assert.Empty(result.Characters);
    }

    [Fact]
    public void AddCharacterKeepsLikelyCharacterName()
    {
        var service = new WorkbenchEditingService(new InMemoryWorkflowLogger());
        var project = EmptyProject();

        var result = service.AddCharacter(project, "陈默");

        var character = Assert.Single(result.Characters);
        Assert.Equal("陈默", character.Name);
    }

    private static InteractionProject EmptyProject()
    {
        return new InteractionProject("empty", "empty", new DraftAnalyzer().Preprocess(string.Empty), [], [], [], []);
    }
}

