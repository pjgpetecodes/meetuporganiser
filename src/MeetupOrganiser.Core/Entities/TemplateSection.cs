namespace MeetupOrganiser.Core.Entities;

public class TemplateSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Markdown { get; set; } = "{{Value}}";
}
