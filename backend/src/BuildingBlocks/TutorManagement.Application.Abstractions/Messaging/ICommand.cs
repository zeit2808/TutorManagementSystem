using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Application.Abstractions.Messaging;

public interface ICommand;

public interface ICommand<TResponse>;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
