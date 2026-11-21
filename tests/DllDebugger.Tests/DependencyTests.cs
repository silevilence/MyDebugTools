using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class DependencyTests
{
    private static string Fixture(string path) => Path.Combine(AppContext.BaseDirectory, "dependencies", path);

    [Fact]
    public void StaticImportsPreserveEdgesAndTerminateCycles()
    {
        var analysis = DependencyAnalysis.Read(Fixture("Root.dll"));
        Assert.Equal(4, analysis.Modules.Count);
        var root = analysis.Root;
        var left = Assert.Single(root.Imports, e => e.Name == "LeftBranch.dll");
        var later = Assert.Single(root.Imports, e => e.Delayed);
        Assert.False(left.Delayed);
        Assert.Equal("Later.dll", later.Name);
        var first = Assert.Single(left.Target!.Imports);
        var shared = Assert.Single(later.Target!.Imports);
        Assert.Equal(DependencyReference.Shared, shared.Reference);
        Assert.Same(first.Target, shared.Target);
        var cycle = Assert.Single(first.Target!.Imports);
        Assert.Equal(DependencyReference.Cycle, cycle.Reference);
        Assert.Same(root, cycle.Target);
        Assert.Contains("缺失", Assert.Single(root.Imports, e => e.Name == "Choice.dll").Problem);
    }
}
