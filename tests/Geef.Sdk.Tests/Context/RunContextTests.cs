using FluentAssertions;
using Geef.Sdk.Context;
using Xunit;

namespace Geef.Sdk.Tests.Context;

public sealed class RunContextTests
{
    private static readonly ContextKey<string> NameKey = new("test:name");
    private static readonly ContextKey<int> CountKey = new("test:count");
    private static readonly ContextKey<string> OtherKey = new("test:other");

    [Fact]
    public void Empty_context_has_no_keys()
    {
        var ctx = new RunContext();
        ctx.Keys.Should().BeEmpty();
    }

    [Fact]
    public void Set_returns_new_instance_with_value()
    {
        var ctx = new RunContext();
        var updated = ctx.Set(NameKey, "Alice");

        updated.Should().NotBeSameAs(ctx);
        updated.GetRequired(NameKey).Should().Be("Alice");
    }

    [Fact]
    public void Original_context_is_not_mutated_after_Set()
    {
        var ctx = new RunContext();
        _ = ctx.Set(NameKey, "Alice");

        ctx.Contains(NameKey).Should().BeFalse();
    }

    [Fact]
    public void GetRequired_throws_KeyNotFoundException_with_descriptive_message()
    {
        var ctx = new RunContext().Set(NameKey, "Alice");

        var act = () => ctx.GetRequired(CountKey);

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*test:count*")
            .WithMessage("*test:name*");
    }

    [Fact]
    public void TryGet_returns_true_when_key_exists()
    {
        var ctx = new RunContext().Set(CountKey, 42);

        var found = ctx.TryGet(CountKey, out var value);

        found.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryGet_returns_false_when_key_missing()
    {
        var ctx = new RunContext();

        var found = ctx.TryGet(CountKey, out var value);

        found.Should().BeFalse();
        value.Should().Be(default(int));
    }

    [Fact]
    public void Contains_returns_true_for_present_key()
    {
        var ctx = new RunContext().Set(NameKey, "Bob");
        ctx.Contains(NameKey).Should().BeTrue();
    }

    [Fact]
    public void Contains_returns_false_for_missing_key()
    {
        var ctx = new RunContext();
        ctx.Contains(NameKey).Should().BeFalse();
    }

    [Fact]
    public void Set_overwrites_existing_value()
    {
        var ctx = new RunContext()
            .Set(CountKey, 1)
            .Set(CountKey, 99);

        ctx.GetRequired(CountKey).Should().Be(99);
    }

    [Fact]
    public void Remove_creates_new_instance_without_key()
    {
        var ctx = new RunContext().Set(NameKey, "Alice");
        var removed = ctx.Remove(NameKey);

        removed.Contains(NameKey).Should().BeFalse();
        ctx.Contains(NameKey).Should().BeTrue();
    }

    [Fact]
    public void Keys_contains_all_set_keys()
    {
        var ctx = new RunContext()
            .Set(NameKey, "x")
            .Set(CountKey, 1);

        ctx.Keys.Should().Contain(NameKey.Name).And.Contain(CountKey.Name);
    }

    [Fact]
    public void Multiple_sets_chain_immutably()
    {
        var ctx1 = new RunContext();
        var ctx2 = ctx1.Set(NameKey, "A");
        var ctx3 = ctx2.Set(CountKey, 5);
        var ctx4 = ctx3.Set(OtherKey, "Z");

        ctx1.Keys.Should().BeEmpty();
        ctx2.Keys.Should().HaveCount(1);
        ctx3.Keys.Should().HaveCount(2);
        ctx4.Keys.Should().HaveCount(3);
    }
}
