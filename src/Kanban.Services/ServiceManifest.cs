using KanbanTasker.Model;
using KanbanTasker.Model.Services;

namespace KanbanTasker.Services
{
    public class ServiceManifest : IServiceManifest
    {
        public ITaskServices TaskServices { get; }
        public IBoardServices BoardServices { get; }
        public IDatabaseServices DatabaseServices { get; }

        public ServiceManifest(ITaskServices taskServices, IBoardServices boardServices, IDatabaseServices databaseServices)
        {
            TaskServices = taskServices;
            BoardServices = boardServices;
            DatabaseServices = databaseServices;
        }
    }
}