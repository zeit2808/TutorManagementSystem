using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Codes;

/// <summary>
/// Hands out the next free code of an <see cref="Domain.AcademicsCodes"/> kind for a center (D9). Codes are never
/// typed by users; call this right before creating the entity, inside the same unit of work.
/// </summary>
public interface IAcademicsCodeGenerator
{
    Task<string> NextAsync(Guid centerId, CodeKind kind, CancellationToken token);
}
