using System;

namespace KanbanTasker.Model.Dto
{
    /// <summary>
    /// A record with a stable global identity (<see cref="Uuid"/>) and create/update
    /// timestamps. This is what makes backup import idempotent and self-healing: import
    /// upserts by Uuid and the newest <see cref="UpdatedAt"/> wins, so re-importing the same
    /// backup changes nothing and re-importing a good backup repairs corrupted rows — without
    /// ever creating duplicates. Timestamps are UTC "yyyy-MM-dd HH:mm:ss.fff" (lexically sortable).
    /// </summary>
    public interface IStamped
    {
        Guid Uuid { get; set; }
        string CreatedAt { get; set; }
        string UpdatedAt { get; set; }
    }
}
