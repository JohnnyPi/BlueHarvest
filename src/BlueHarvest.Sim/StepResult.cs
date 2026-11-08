namespace BlueHarvest.Sim;

public enum StepResult
{
    /// <summary>An actor took its turn.</summary>
    Acted,

    /// <summary>The next actor due is waiting for input; nothing changed.</summary>
    AwaitingInput,

    /// <summary>No actors are scheduled.</summary>
    Idle,
}
