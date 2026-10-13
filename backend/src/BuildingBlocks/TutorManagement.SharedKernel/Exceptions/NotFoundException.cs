namespace TutorManagement.SharedKernel.Exceptions;

public sealed class NotFoundException(string message) : ApplicationBaseException(message);
