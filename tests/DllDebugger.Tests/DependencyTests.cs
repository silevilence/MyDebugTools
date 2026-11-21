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

    [Theory]
    [InlineData("A", "B")]
    [InlineData("B", "A")]
    public void FirstCompatibleDirectoryReplacesTheWholeBranch(string first, string second)
    {
        var analysis = DependencyAnalysis.Read(Fixture("Root.dll"), [Fixture(first), Fixture(second)]);
        var choice = Assert.Single(analysis.Root.Imports, e => e.Name == "Choice.dll").Target!;
        Assert.Equal(Fixture($@"{first}\Choice.dll"), choice.Path);
        Assert.Equal("x64", choice.Architecture);
        Assert.Equal(Fixture($@"{first}\Leaf{first}.dll"), Assert.Single(choice.Imports).Target!.Path);
        Assert.DoesNotContain(analysis.Modules, m => Path.GetDirectoryName(m.Path) == Fixture(second));
    }

    [Fact]
    public void IncompatibleCandidateExplainsFailureAndAllowsALaterMatch()
    {
        var wrong = Directory.CreateTempSubdirectory();
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "x86", "fixture.dll"), Path.Combine(wrong.FullName, "Choice.dll"));
            DependencyEdge Choice(params string[] dirs) =>
                Assert.Single(DependencyAnalysis.Read(Fixture("Root.dll"), dirs).Root.Imports, e => e.Name == "Choice.dll");
            var failed = Choice(wrong.FullName);
            Assert.Null(failed.Target);
            Assert.Contains("x86", failed.Problem);
            Assert.Contains("x64", failed.Problem);
            Assert.Equal(Fixture(@"A\Choice.dll"), Choice(wrong.FullName, Fixture("A")).Target!.Path);
        }
        finally { wrong.Delete(true); }
    }

    [Fact]
    public void ContractIsDistinctFromItsLocalHostAndMissingMapping()
    {
        var analysis = DependencyAnalysis.Read(Fixture("Contracts.dll"));
        var contract = Assert.Single(analysis.Root.Imports, e => e.Name.StartsWith("api-ms-win-"));
        Assert.True(contract.IsApiSet);
        Assert.Equal("kernelbase.dll", contract.ApiSetHost, ignoreCase: true);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, contract.ApiSetHost!), contract.Target!.Path, ignoreCase: true);
        var missing = Assert.Single(analysis.Root.Imports, e => e.Target == null);
        Assert.True(missing.IsApiSet);
        Assert.Contains("没有可用宿主", missing.Problem);
    }
}
