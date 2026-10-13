namespace TutorManagement.SharedKernel.Exceptions;

public sealed class ConflictException(string message) : ApplicationBaseException(message);
