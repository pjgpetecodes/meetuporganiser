using System.Data;
using MeetupOrganiser.Core.Entities;
using MeetupOrganiser.Core.Interfaces;
using MeetupOrganiser.Core.Services;
using MeetupOrganiser.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MeetupOrganiser.Infrastructure.Services;

public class TemplateSectionService(IDbContextFactory<AppDbContext> factory) : ITemplateSectionService
{
    public async Task<List<TemplateSection>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.TemplateSections.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);
    }

    public Task SaveAsync(TemplateSection section, CancellationToken cancellationToken = default) =>
        ChangeAsync(section.Id, section, cancellationToken);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, null, cancellationToken);

    private async Task ChangeAsync(Guid id, TemplateSection? change, CancellationToken cancellationToken)
    {
        await using var strategyDb = await factory.CreateDbContextAsync(cancellationToken);
        await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            var sections = await db.TemplateSections.OrderBy(s => s.Name).ToListAsync(cancellationToken);
            var stored = sections.SingleOrDefault(s => s.Id == id);
            if (change is null)
            {
                if (stored is null) throw new ArgumentException("The section was already deleted. Reload the list.");
                sections.Remove(stored);
                db.TemplateSections.Remove(stored);
            }
            else
            {
                var name = change.Name.Trim();
                if (stored is not null && MeetupDescriptionTemplate.IsReservedName(stored.Name) &&
                    !stored.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"'{stored.Name}' is a reserved built-in placeholder and cannot be renamed. You can edit its section content.");
                if (MeetupDescriptionTemplate.IsReservedName(name) &&
                    (stored is null || !stored.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException($"'{name}' is a reserved built-in placeholder. Choose a different name for your custom section.");
                if (stored is null)
                {
                    stored = new TemplateSection { Id = id };
                    sections.Add(stored);
                    db.TemplateSections.Add(stored);
                }
                stored.Name = name;
                stored.Description = change.Description.Trim();
                stored.Markdown = change.Markdown;
            }
            MeetupDescriptionTemplate.ValidateSections(sections);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        });
    }
}
