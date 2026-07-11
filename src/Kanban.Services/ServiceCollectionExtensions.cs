using KanbanTasker.Model;
using KanbanTasker.Model.Services;
using KanbanTasker.Services.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KanbanTasker.Services
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Active connection string, set at startup from appsettings.json
        /// (see <see cref="DatabaseConfig"/>). Never hardcode credentials here.
        /// The AddDbContext options lambda reads this at scope creation, so it can be
        /// assigned after DI is built but before the first database access.
        /// </summary>
        public static string ConnectionString { get; set; } = "";

        /// <summary>
        /// Registers the Kanban services backed by SQL Server Express (the app's only backend).
        /// </summary>
        public static IServiceCollection AddKanbanServices(this IServiceCollection services)
        {
            services.AddDbContext<Database.Db>(options =>
                options.UseSqlServer(ConnectionString));

            services.AddScoped<IBoardServices, BoardServices>();
            services.AddScoped<ITaskServices, TaskServices>();
            services.AddScoped<IDatabaseServices, DatabaseServices>();

            // ServiceManifest bundles the services for the ViewModels
            services.AddScoped<IServiceManifest>(sp =>
            {
                var board = sp.GetRequiredService<IBoardServices>();
                var task = sp.GetRequiredService<ITaskServices>();
                var dbs = sp.GetRequiredService<IDatabaseServices>();
                return new ServiceManifest(task, board, dbs);
            });

            return services;
        }
    }
}
