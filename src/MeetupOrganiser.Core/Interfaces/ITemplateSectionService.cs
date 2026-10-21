using MeetupOrganiser.Core.Entities;

namespace MeetupOrganiser.Core.Interfaces;

public interface ITemplateSectionService
{
    Task<List<TemplateSection>> ListAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TemplateSection section, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
