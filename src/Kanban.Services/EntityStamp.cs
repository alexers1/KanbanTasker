using System;
using KanbanTasker.Model.Dto;

namespace KanbanTasker.Services
{
    /// <summary>
    /// Stamps entities with a stable <see cref="IStamped.Uuid"/> and UTC create/update
    /// timestamps. Every board/column/task gets these when it is first created and its
    /// <see cref="IStamped.UpdatedAt"/> is bumped on each save — which is what makes backup
    /// import idempotent (upsert by Uuid, newest wins).
    /// </summary>
    public static class EntityStamp
    {
        /// <summary>UTC, lexically sortable, matches the SQL backfill format (CONVERT style 121).</summary>
        public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

        /// <summary>Stamp a brand-new entity: assign a Uuid and set both timestamps.</summary>
        public static T Create<T>(T e) where T : IStamped
        {
            if (e.Uuid == Guid.Empty) e.Uuid = Guid.NewGuid();
            var now = Now();
            if (string.IsNullOrEmpty(e.CreatedAt)) e.CreatedAt = now;
            e.UpdatedAt = now;
            return e;
        }

        /// <summary>Bump an existing entity's UpdatedAt (preserving Uuid/CreatedAt supplied by the caller).</summary>
        public static T Touch<T>(T e) where T : IStamped
        {
            if (e.Uuid == Guid.Empty) e.Uuid = Guid.NewGuid();
            if (string.IsNullOrEmpty(e.CreatedAt)) e.CreatedAt = Now();
            e.UpdatedAt = Now();
            return e;
        }
    }
}
