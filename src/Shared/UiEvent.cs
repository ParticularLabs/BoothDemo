namespace Shared;

public abstract record UiEvent;

public record ProcessingStarted(string MessageId) : UiEvent;
public record StageProgress(string MessageId, ProcessingStage Stage, double Percent) : UiEvent;
public record StageCompleted(string MessageId, ProcessingStage Stage) : UiEvent;
public record ProcessingCompleted(string MessageId) : UiEvent;
public record ControlStateChanged(string Text) : UiEvent;
public record ProcessingFailed(string MessageId, string Error) : UiEvent;
public record SendingStarted(int BatchNumber, int BatchSize) : UiEvent;
public record SendingProgress(double Percent) : UiEvent;
public record SendingCompleted() : UiEvent;
public record OrderSagaStarted(string OrderId) : UiEvent;
public record OrderSagaCompleted(string OrderId) : UiEvent;
