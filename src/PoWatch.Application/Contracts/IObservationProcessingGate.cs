namespace PoWatch.Application.Contracts;

public interface IObservationProcessingGate
{
    bool TryEnter();

    void Exit();

    /// <summary>Returns true when the gate is currently held by an active inference cycle.</summary>
    bool IsProcessing { get; }
}
