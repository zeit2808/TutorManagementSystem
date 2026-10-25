using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Subjects.GetSubject;

public sealed record GetSubjectQuery(Guid CenterId, Guid SubjectId) : IQuery<SubjectDto>;

public sealed class GetSubjectQueryHandler(ISubjectRepository subjects)
    : IQueryHandler<GetSubjectQuery, SubjectDto>
{
    public async Task<Result<SubjectDto>> HandleAsync(GetSubjectQuery query, CancellationToken cancellationToken)
    {
        var subject = await subjects.FindAsync(query.CenterId, query.SubjectId, cancellationToken);
        return subject is null
            ? Result<SubjectDto>.Failure(SubjectErrors.NotFound)
            : Result<SubjectDto>.Success(SubjectDto.From(subject));
    }
}
