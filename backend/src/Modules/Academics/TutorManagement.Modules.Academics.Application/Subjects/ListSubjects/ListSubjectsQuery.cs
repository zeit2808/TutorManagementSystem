using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Subjects.ListSubjects;

public sealed record ListSubjectsQuery(Guid CenterId, bool IncludeArchived) : IQuery<List<SubjectDto>>;

public sealed class ListSubjectsQueryHandler(ISubjectRepository subjects)
    : IQueryHandler<ListSubjectsQuery, List<SubjectDto>>
{
    public async Task<Result<List<SubjectDto>>> HandleAsync(ListSubjectsQuery query, CancellationToken cancellationToken)
    {
        var items = await subjects.ListAsync(query.CenterId, query.IncludeArchived, cancellationToken);
        return Result<List<SubjectDto>>.Success(items.Select(SubjectDto.From).ToList());
    }
}
