using Satli_Gui.Services;
using Xunit;

namespace Satli.Gui.Tests;

public sealed class CliElevationPolicyTests
{
    public static TheoryData<string[], bool> Cases => new()
    {
        { ["scan", "--jsonl"], false },
        { ["status", "--jsonl"], false },
        { ["cache", "refresh", "--jsonl"], false },
        { ["install", "730", "--dry-run", "--jsonl"], false },
        { ["restore", "730", "--dry-run", "--jsonl"], false },
        { ["local-import", "translation.bin", "--dry-run", "--jsonl"], false },
        { ["schema", "inspect", "730", "--jsonl"], false },
        { ["schema", "draft", "730", "--jsonl"], false },
        { ["schema", "export", "730", "--jsonl"], false },
        { ["schema", "revisions", "list", "730", "--jsonl"], false },
        { ["schema", "revisions", "export", "730", "abc123", "--jsonl"], false },
        { ["petition", "export", "730", "--jsonl"], false },
        { ["install", "730", "--yes", "--jsonl"], true },
        { ["restore", "730", "--yes", "--jsonl"], true },
        { ["local-import", "translation.bin", "--yes", "--jsonl"], true },
        { ["schema", "apply", "730", "--yes", "--jsonl"], true },
        { ["schema", "restore", "730", "--yes", "--jsonl"], true },
        { ["schema", "revisions", "activate", "730", "abc123", "--yes", "--jsonl"], true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void RequiresElevation_ClassifiesCliOperations(string[] arguments, bool expected)
    {
        Assert.Equal(expected, CliElevationPolicy.RequiresElevation(arguments));
    }

    [Fact]
    public void RequiresElevation_RejectsEmptyOrIncompleteCommands()
    {
        Assert.False(CliElevationPolicy.RequiresElevation([]));
        Assert.False(CliElevationPolicy.RequiresElevation(["schema"]));
        Assert.False(CliElevationPolicy.RequiresElevation(["schema", "revisions"]));
    }
}
