namespace PoWatch.Application.Contracts;

public interface IObservationProcessingGate
{
    bool TryEnter();

    void Exit();
}
