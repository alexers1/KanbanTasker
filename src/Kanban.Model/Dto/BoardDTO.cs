using System;
using System.Collections.Generic;
using System.Text;

namespace KanbanTasker.Model.Dto
{
    /// <summary>
    /// A data transfer object for a kanban board
    /// </summary>
    public class BoardDTO : IStamped
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Notes { get; set; } = "";

        // Stable identity + timestamps for idempotent import (see IStamped).
        public Guid Uuid { get; set; }
        public string CreatedAt { get; set; } = "";
        public string UpdatedAt { get; set; } = "";

        public virtual ICollection<TaskDTO> Tasks { get; set; } = new List<TaskDTO>();
    }
}