namespace Blurt.Engine.Permissions;

/// <summary>The first-run setup pages, in the order the wizard presents them.</summary>
public enum SetupStep
{
    /// <summary>Connect an AssemblyAI API key.</summary>
    ApiKey,

    /// <summary>Confirm microphone access.</summary>
    Microphone,

    /// <summary>Pick the trigger key and how it activates.</summary>
    Shortcut,

    /// <summary>Pick the start/stop cue voice.</summary>
    Sound,
}

/// <summary>
/// The setup wizard's navigation: which step shows, when Continue is allowed, and whether the main
/// window shows the ready view. The ready view shows only while setup is ready and the wizard has
/// been completed; an install that is ready when the wizard is created counts as completed.
/// Losing readiness shows the wizard on the first requirement that is missing; a completed wizard
/// returns to the ready view as soon as setup is ready again.
/// </summary>
public sealed class SetupWizard
{
    /// <summary>Creates the wizard on the API key step, already completed when <paramref name="isReady"/>.</summary>
    public SetupWizard(bool isReady)
    {
        IsCompleted = isReady;
    }

    /// <summary>Every step, in presentation order.</summary>
    public static IReadOnlyList<SetupStep> Steps { get; } =
        [SetupStep.ApiKey, SetupStep.Microphone, SetupStep.Shortcut, SetupStep.Sound];

    /// <summary>The step on screen while the wizard shows.</summary>
    public SetupStep Step { get; private set; } = SetupStep.ApiKey;

    /// <summary>Whether the user has finished the wizard (or setup was ready from the start).</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>The one-based position of <see cref="Step"/>.</summary>
    public int StepNumber => Index(Step) + 1;

    /// <summary>Whether a Back button applies: every step but the first.</summary>
    public bool CanGoBack => Index(Step) > 0;

    /// <summary>Whether <see cref="Step"/> is the last one, whose Continue finishes the wizard.</summary>
    public bool IsLastStep => Index(Step) == Steps.Count - 1;

    /// <summary>
    /// Whether Continue is enabled on <see cref="Step"/>: the API key step needs a stored key, the
    /// microphone step needs access, and the last step needs setup to be ready as a whole.
    /// </summary>
    public bool CanContinue(PermissionStatus permissions, bool hasApiKey) => Step switch
    {
        SetupStep.ApiKey => hasApiKey,
        SetupStep.Microphone => permissions.Microphone,
        SetupStep.Sound => SetupReadiness.IsReady(permissions, hasApiKey),
        _ => true,
    };

    /// <summary>
    /// Advances one step, or completes the wizard from the last step. Does nothing when
    /// <see cref="CanContinue"/> is false. Returns whether anything changed.
    /// </summary>
    public bool Continue(PermissionStatus permissions, bool hasApiKey)
    {
        if (!CanContinue(permissions, hasApiKey))
        {
            return false;
        }

        if (IsLastStep)
        {
            IsCompleted = true;
            return true;
        }

        Step = Steps[Index(Step) + 1];
        return true;
    }

    /// <summary>Moves back one step. Returns false on the first step, which has nothing before it.</summary>
    public bool Back()
    {
        if (!CanGoBack)
        {
            return false;
        }

        Step = Steps[Index(Step) - 1];
        return true;
    }

    /// <summary>Moves the wizard to <paramref name="step"/>, which shows whenever setup is not ready.</summary>
    public void Show(SetupStep step)
    {
        Step = step;
    }

    /// <summary>
    /// Moves the wizard to the first requirement missing after setup stopped being ready: the API
    /// key, then the microphone. With neither missing the step is left as it was.
    /// </summary>
    public void ReadinessLost(PermissionStatus permissions, bool hasApiKey)
    {
        if (!hasApiKey)
        {
            Step = SetupStep.ApiKey;
        }
        else if (!permissions.Microphone)
        {
            Step = SetupStep.Microphone;
        }
    }

    /// <summary>Whether the main window shows the ready view rather than the wizard.</summary>
    public bool ShowsReadyView(bool isReady) => isReady && IsCompleted;

    private static int Index(SetupStep step) => step switch
    {
        SetupStep.ApiKey => 0,
        SetupStep.Microphone => 1,
        SetupStep.Shortcut => 2,
        SetupStep.Sound => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };
}
