namespace TutorManagement.SharedKernel.Exceptions;

public sealed class BusinessRuleException(string message) : ApplicationBaseException(message);
