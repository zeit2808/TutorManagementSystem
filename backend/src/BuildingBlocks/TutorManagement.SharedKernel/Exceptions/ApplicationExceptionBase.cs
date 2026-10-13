using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.SharedKernel.Exceptions;

public abstract class ApplicationBaseException : Exception
{
    protected ApplicationBaseException(string message, IReadOnlyCollection<Error>? errors = null)
        : base(message)
    {
        Errors = errors ?? [];
    }

    public IReadOnlyCollection<Error> Errors { get; }
}
