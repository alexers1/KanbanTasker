using KanbanTasker.Model;
using KanbanTasker.Model.Services;

namespace Kanban_WinUI.Services.Api
{
    /// <summary>
    /// Client-side IServiceManifest wired to the HttpClient-backed services
    /// (replaces the EF ServiceManifest that lived in Kanban.Services).
    /// </summary>
    public class ApiServiceManifest : IServiceManifest
    {
        public ITaskServices TaskServices { get; }
        public IBoardServices BoardServices { get; }
        public IDatabaseServices DatabaseServices { get; }

        public ApiServiceManifest(ITaskServices taskServices, IBoardServices boardServices, IDatabaseServices databaseServices)
        {
            TaskServices = taskServices;
            BoardServices = boardServices;
            DatabaseServices = databaseServices;
        }
    }
}
