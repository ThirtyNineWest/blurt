using Blurt.Engine.FocusCapture;
using Blurt.Engine.Windows.FocusCapture;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.FocusCapture;

public sealed class FieldContextRulesTests
{
    private static Func<(string?, string?)> Text(string? prior, string? selected) => () => (prior, selected);

    [Test]
    public void AnUnreadableRoleFailsClosedAndReadsNothing()
    {
        var reads = 0;
        string? Count()
        {
            reads++;
            return "x";
        }

        var context = FieldContextRules.Compose(
            isPassword: null,
            sameProcess: true,
            Count,
            Count,
            () => { reads++; return ("x", "x"); });

        context.Should().Be(FocusedFieldContext.Empty with { IsSecure = true });
        reads.Should().Be(0);
    }

    [Test]
    public void APasswordFieldKeepsOnlyTitleAndLabel()
    {
        var textRead = false;

        var context = FieldContextRules.Compose(
            isPassword: true,
            sameProcess: true,
            () => "Sign in",
            () => "Password",
            () => { textRead = true; return ("hunter2", "hunter2"); });

        context.Should().Be(new FocusedFieldContext("Sign in", "Password", null, null, IsSecure: true));
        textRead.Should().BeFalse();
    }

    [Test]
    public void AnElementInAnotherProcessHasNoTextRead()
    {
        var textRead = false;

        var context = FieldContextRules.Compose(
            isPassword: false,
            sameProcess: false,
            () => "Doc",
            () => "Body",
            () => { textRead = true; return ("prior", null); });

        context.Should().Be(new FocusedFieldContext("Doc", "Body", null, null, IsSecure: false));
        textRead.Should().BeFalse();
    }

    [Test]
    public void AnOrdinaryFieldReadsEveryValue()
    {
        var context = FieldContextRules.Compose(false, true, () => " Doc ", () => "Body", Text("Hello ", " sel "));

        context.Should().Be(new FocusedFieldContext("Doc", "Body", "Hello ", "sel", IsSecure: false));
    }

    [Test]
    public void AnAllInvisiblePriorReadBecomesNull()
    {
        var context = FieldContextRules.Compose(false, true, () => null, () => null, Text("​", "​"));

        context.PriorText.Should().BeNull();
        context.SelectedText.Should().BeNull();
    }

    [Test]
    public void EveryValueIsClippedToItsCap()
    {
        var context = FieldContextRules.Compose(
            false,
            true,
            () => new string('t', 200),
            () => new string('l', 200),
            Text(new string('a', 10) + new string('p', 400), new string('s', 400)));

        context.WindowTitle.Should().HaveLength(FocusCaptureRules.MaxWindowTitleChars);
        context.FieldLabel.Should().HaveLength(FocusCaptureRules.MaxFieldLabelChars);
        context.PriorText.Should().Be(new string('p', FocusCaptureRules.MaxPriorChars));
        context.SelectedText.Should().HaveLength(FocusCaptureRules.MaxSelectedChars);
    }

    [Test]
    public void AThrowReadingTheFocusedElementFailsClosed()
    {
        var captured = false;

        var context = FieldContextRules.FromFocusedElement<string>(
            () => throw new InvalidOperationException("unavailable"),
            _ =>
            {
                captured = true;
                return FocusedFieldContext.Empty;
            });

        context.Should().Be(FocusedFieldContext.Empty with { IsSecure = true });
        captured.Should().BeFalse();
    }

    [Test]
    public void NoFocusedElementIsEmptyAndNotSecure()
    {
        var captured = false;

        var context = FieldContextRules.FromFocusedElement<string>(
            () => null,
            _ =>
            {
                captured = true;
                return FocusedFieldContext.Empty;
            });

        context.Should().Be(FocusedFieldContext.Empty);
        context.IsSecure.Should().BeFalse();
        captured.Should().BeFalse();
    }

    [Test]
    public void AFocusedElementIsHandedToTheCapture()
    {
        var expected = new FocusedFieldContext("Doc", "Body", "prior", null, IsSecure: false);

        var context = FieldContextRules.FromFocusedElement(() => "element", element => element == "element" ? expected : FocusedFieldContext.Empty);

        context.Should().Be(expected);
    }
}
