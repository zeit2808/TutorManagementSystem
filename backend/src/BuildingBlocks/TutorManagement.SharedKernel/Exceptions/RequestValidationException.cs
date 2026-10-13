using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.SharedKernel.Exceptions;

public sealed class RequestValidationException(IReadOnlyCollection<Error> errors)
    : ApplicationBaseException("The request is invalid.", errors);
