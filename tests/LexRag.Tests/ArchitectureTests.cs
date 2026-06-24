using FluentAssertions;
using LexRag.Core.Models;
using NetArchTest.Rules;

namespace LexRag.Tests;

public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Core = typeof(RagAnswer).Assembly;

    [Fact]
    public void Core_does_not_depend_on_any_outer_layer()
    {
        var result = Types.InAssembly(Core)
            .Should()
            .NotHaveDependencyOnAny(
                "LexRag.Ingestion", "LexRag.Index", "LexRag.Retrieval",
                "LexRag.Orchestration", "LexRag.Eval", "LexRag.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the domain core must stay adapter-free (hexagonal): {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Ports_are_interfaces()
    {
        var result = Types.InAssembly(Core)
            .That().ResideInNamespace("LexRag.Core.Abstractions")
            .Should().BeInterfaces()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
