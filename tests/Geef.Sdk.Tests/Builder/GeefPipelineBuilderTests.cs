using FluentAssertions;
using Geef.Sdk.Context;
using Geef.Sdk.Exceptions;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using NSubstitute;
using Xunit;

namespace Geef.Sdk.Tests.Builder;

public sealed class GeefPipelineBuilderTests
{
    private static IGroundingStep MakeGrounding()
    {
        var g = Substitute.For<IGroundingStep>();
        g.RunAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GroundingResult { Context = new RunContext() }));
        return g;
    }

    private static IExecutionStep MakeExecution()
    {
        var e = Substitute.For<IExecutionStep>();
        e.RunAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(new ExecutionResult { UpdatedContext = c.Arg<IRunContext>() }));
        return e;
    }

    private static IReviewer MakeApprover()
    {
        var r = Substitute.For<IReviewer>();
        r.Name.Returns("Approver");
        r.Priority.Returns(100);
        r.ReviewAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReviewResult { ReviewerName = "Approver", Decision = ReviewDecision.Approved }));
        return r;
    }

    private static IFinalizer<string> MakeFinalizer()
    {
        var f = Substitute.For<IFinalizer<string>>();
        f.FinalizeAsync(Arg.Any<IRunContext>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult(new FinalizeResult<string> { Output = "done", FinalContext = c.Arg<IRunContext>() }));
        return f;
    }

    [Fact]
    public void Build_succeeds_with_all_required_components()
    {
        var act = () => Geef.CreatePipeline<string>()
            .UseGrounding(MakeGrounding())
            .UseExecution(MakeExecution())
            .AddReviewer(MakeApprover())
            .UseFinalizer(MakeFinalizer())
            .Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_throws_when_grounding_missing()
    {
        var act = () => Geef.CreatePipeline<string>()
            .UseExecution(MakeExecution())
            .AddReviewer(MakeApprover())
            .UseFinalizer(MakeFinalizer())
            .Build();

        act.Should().Throw<PipelineConfigurationException>()
            .WithMessage("*Grounding*");
    }

    [Fact]
    public void Build_throws_when_execution_missing()
    {
        var act = () => Geef.CreatePipeline<string>()
            .UseGrounding(MakeGrounding())
            .AddReviewer(MakeApprover())
            .UseFinalizer(MakeFinalizer())
            .Build();

        act.Should().Throw<PipelineConfigurationException>()
            .WithMessage("*Execution*");
    }

    [Fact]
    public void Build_throws_when_no_reviewers()
    {
        var act = () => Geef.CreatePipeline<string>()
            .UseGrounding(MakeGrounding())
            .UseExecution(MakeExecution())
            .UseFinalizer(MakeFinalizer())
            .Build();

        act.Should().Throw<PipelineConfigurationException>()
            .WithMessage("*reviewer*");
    }

    [Fact]
    public void Build_throws_when_finalizer_missing()
    {
        var act = () => Geef.CreatePipeline<string>()
            .UseGrounding(MakeGrounding())
            .UseExecution(MakeExecution())
            .AddReviewer(MakeApprover())
            .Build();

        act.Should().Throw<PipelineConfigurationException>()
            .WithMessage("*Finalizer*");
    }

    [Fact]
    public void Default_convergence_policy_is_DefaultConvergencePolicy()
    {
        var builder = Geef.CreatePipeline<string>()
            .UseGrounding(MakeGrounding())
            .UseExecution(MakeExecution())
            .AddReviewer(MakeApprover())
            .UseFinalizer(MakeFinalizer());

        var runner = builder.Build();
        runner.Should().NotBeNull();
    }

    [Fact]
    public void Custom_convergence_policy_is_accepted()
    {
        var policy = Substitute.For<IConvergencePolicy>();
        var act = () => Geef.CreatePipeline<string>()
            .UseGrounding(MakeGrounding())
            .UseExecution(MakeExecution())
            .AddReviewer(MakeApprover())
            .UseFinalizer(MakeFinalizer())
            .UseConvergencePolicy(policy)
            .Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddReviewer_throws_on_null()
    {
        var act = () => Geef.CreatePipeline<string>().AddReviewer(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
